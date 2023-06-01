#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Dapper;
using Microservices.Models.Dto;
using Microservices.Models.Entities;
using Microservices.Services.Abstractions;
using Microservices.Utils.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;

namespace Microservices;

public class FunctionContainer
{
    private readonly ComputerVisionClient computerVisionClient;
    private readonly IDatabaseConnectionFactory connectionFactory;
    private readonly ILogger<FunctionContainer> logger;

    public FunctionContainer(ComputerVisionClient computerVisionClient,
        IDatabaseConnectionFactory connectionFactory,
        ILoggerFactory loggerFactory)
    {
        this.computerVisionClient = computerVisionClient;
        this.connectionFactory = connectionFactory;
        logger = loggerFactory.CreateLogger<FunctionContainer>();
    }

    [FunctionName("CheckImageForInappropriateContent")]
    public async Task<IActionResult> CheckImageForInappropriateContent([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "check/{userId:alpha?}")] HttpRequest request, string? userId,
        [Queue("description-tickets")] QueueClient descriptionTicketsQueue, [Blob("images/{rand-guid}")] BlobClient blobClient)
    {
        await using var imageStream = new MemoryStream();
        await request.Body.CopyToAsync(imageStream);
        imageStream.Position = 0;
        await using var imageStreamClone = new MemoryStream();
        await imageStream.CopyToAsync(imageStreamClone);
        
        logger.LogInformation("{BaseLogMessage}: receive a request to check image for inappropriate content {UserId}",
            GetBaseLogMessage(nameof(CheckImageForInappropriateContent)), $"{(!string.IsNullOrWhiteSpace(userId) ? $"from user {userId}" : string.Empty)}");
    
        ImageAnalysis response = default!;
        string? errorMessage = default;
        try
        {
            imageStream.Position = 0;
            response = await computerVisionClient.AnalyzeImageInStreamAsync(imageStream, new List<VisualFeatureTypes?>
            {
                VisualFeatureTypes.Adult
            });
            imageStream.Close();
        }
        catch (ComputerVisionErrorResponseException exception)
        {
            errorMessage = exception.Body.Error.Message;
        }
        catch (Exception exception)
        {
            errorMessage = exception.Message;
        }
    
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            logger.LogWarning(
                "{BaseLogMessage}: image was sent to the checking for inappropriate content, but the received response was unsuccessful. Message: {ErrorMessage}",
                GetBaseLogMessage(nameof(DescribeImage)), errorMessage);
        }
    
        if (response.Adult?.IsInappropriateContent() is true)
        {
            if (!string.IsNullOrWhiteSpace(userId))
            {
                const string Query = "UPDATE Users SET LockoutEnabled = @NewLockoutEnabled WHERE Id = @UserId";
                using var connection = connectionFactory.CreateConnection();
                await connection.ExecuteAsync(Query, new
                {
                    NewLockoutEnabled = true,
                    userId
                });
                connection.Close();
    
                logger.LogInformation("{BaseLogMessage}: user '{UserId}' has been banned for uploading an image with inappropriate content",
                    GetBaseLogMessage(nameof(CheckImageForInappropriateContent)), userId);
            }
    
            logger.LogWarning(
                "{BaseLogMessage}: image has been detected to contain inappropriate content. Removing an image from processing pipeline",
                GetBaseLogMessage(nameof(CheckImageForInappropriateContent)));
            
            return new BadRequestResult();
        }
    
        logger.LogInformation(
            "{BaseLogMessage}: image was successfully checked for inappropriate content. Saving it",
            nameof(CheckImageForInappropriateContent));

        imageStreamClone.Position = 0;
        await blobClient.UploadAsync(imageStreamClone);

        logger.LogInformation(
            "{BaseLogMessage}: image was successfully saved at URL '{ImageUrl}'. Saving it to database",
            nameof(CheckImageForInappropriateContent), blobClient.Uri);
        
        logger.LogInformation(
            "{BaseLogMessage}: image was successfully processed, sending it next to processing pipeline",
            nameof(CheckImageForInappropriateContent));
    
        var ticket = new TicketDto { UserId = userId, ImageUrl = blobClient.Uri.ToString() };
        await descriptionTicketsQueue.SendMessageAsync(JsonSerializer.Serialize(ticket));
        return new OkResult();
    }

    [FunctionName("DescribeImage")]
    public async Task DescribeImage([QueueTrigger("description-tickets")] TicketDto ticket)
    {
        logger.LogInformation("{BaseLogMessage}: received a request to describe image by URL '{ImageUrl}'",
            GetBaseLogMessage(nameof(DescribeImage)), ticket.ImageUrl);

        string? errorMessage = null;
        IHttpOperationResponse<ImageDescription> response = default!;
        try
        {
            response = await computerVisionClient.DescribeImageWithHttpMessagesAsync(ticket.ImageUrl);
            if (!response.Response.IsSuccessStatusCode)
            {
                errorMessage = response.Response.ReasonPhrase;
            }
        }
        catch (ComputerVisionErrorResponseException exception)
        {
            errorMessage = exception.Body.Error.Message;
        }
        catch (Exception exception)
        {
            errorMessage = exception.Message;
        }

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            logger.LogWarning(
                "{BaseLogMessage}: image '{ImageUrl}' was sent to the describing, but the received response was unsuccessful. Message: {ErrorMessage}",
                GetBaseLogMessage(nameof(DescribeImage)), ticket.ImageUrl, response.Response.ReasonPhrase);
            return;
        }

        var description = response.Body.Captions.First()
            .Text;
        var image = new Image { Url = ticket.ImageUrl, Description = description, UserId = ticket.UserId };

        const string Query = "INSERT INTO Images (Url, Description, UserId) VALUES (@Url, @Description, @UserId);";
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(Query, image);
        connection.Close();

        logger.LogInformation("{BaseLogMessage}: successfully saved described image '{ImageUrl}' with description '{Description}'",
            GetBaseLogMessage(nameof(DescribeImage)), ticket.ImageUrl, description);
    }

    #region Utils

    private static string GetBaseLogMessage(string methodName, HttpRequest? request = null)
    {
        var messageBuilder = new StringBuilder();
        messageBuilder.Append('[')
            .Append(request is not null ? request.Method : "UTILITY")
            .Append($"] {nameof(FunctionContainer)}.{methodName}");

        return messageBuilder.ToString();
    }

    #endregion
}