namespace LocaForge.Core
{
    /// <summary>Open a form only; users review and submit it themselves in GitHub.</summary>
    public static class CommunityLinks
    {
        static string Issues => "https://github.com/" + UpdateService.Repo + "/issues/new?template=";
        public static string Feedback => Issues + "bug-report.yml";
        public static string TranslationRequest => Issues + "translation-request.yml";
    }
}
