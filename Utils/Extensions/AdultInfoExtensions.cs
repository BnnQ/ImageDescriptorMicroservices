using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;

namespace Microservices.Utils.Extensions;

public static class AdultInfoExtensions
{
    public static bool IsInappropriateContent(this AdultInfo info)
    {
        return info.IsAdultContent || info.IsGoryContent || info.IsRacyContent;
    }
}