using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Horizon.Plugin.Deadlocked
{
    public class AppSettings
    {
        /// <summary>
        /// This settings respective app id.
        /// </summary>
        public int AppId { get; }

        /// <summary>
        /// Whether or not the patch is configured to send.
        /// </summary>
        public bool EnablePatch { get; private set; } = true;

        /// <summary>
        /// Whether or not the unpatch is configured to send.
        /// 
        /// Will always be sent when EnablePatch is true. This is only meaningful when EnablePatch is false.
        /// </summary>
        public bool EnableUnpatch { get; private set; } = true;

        /// <summary>
        /// When set, overrides the default patch-APPID.bin patch filename with patch-OVERRIDENAME-APPID.bin
        /// </summary>
        public string PatchOverrideName { get; private set; } = null;

        /// <summary>
        /// 
        /// </summary>
        public DateTimeOffset? ScavengerHuntBeginDate { get; private set; } = null;

        /// <summary>
        /// 
        /// </summary>
        public DateTimeOffset? ScavengerHuntEndDate { get; private set; } = null;

        /// <summary>
        /// 
        /// </summary>
        public float ScavengerHuntSpawnRateFactor { get; private set; } = 1f;

        /// <summary>
        /// 
        /// </summary>
        public string BannerImageBase64 { get; private set; } = null;

        public AppSettings(int appId)
        {
            AppId = appId;
        }

        public void SetSettings(Dictionary<string, string> settings)
        {
            string prefix = Server.Medius.Program.Database.GetUsername();
            string value = null;

            // EnablePatch
            if (settings.TryGetValue($"{prefix}_EnablePatch", out value) && bool.TryParse(value, out var enablePatch))
                EnablePatch = enablePatch;
            // EnableUnpatch
            if (settings.TryGetValue($"{prefix}_EnableUnpatch", out value) && bool.TryParse(value, out var enableUnpatch))
                EnableUnpatch = enableUnpatch;
            // PatchOverrideName
            if (settings.TryGetValue($"{prefix}_PatchOverrideName", out value))
                PatchOverrideName = value;
            // ScavengerHuntBeginDate
            if (settings.TryGetValue($"{prefix}_ScavengerHuntBeginDate", out value) && DateTimeOffset.TryParse(value, out var bdt))
                ScavengerHuntBeginDate = bdt;
            else
                ScavengerHuntBeginDate = null;
            // ScavengerHuntEndDate
            if (settings.TryGetValue($"{prefix}_ScavengerHuntEndDate", out value) && DateTimeOffset.TryParse(value, out var edt))
                ScavengerHuntEndDate = edt;
            else
                ScavengerHuntEndDate = null;
            // ScavengerHuntSpawnRateFactor
            if (settings.TryGetValue($"{prefix}_ScavengerHuntSpawnRateFactor", out value) && float.TryParse(value, out var spawnRate))
                ScavengerHuntSpawnRateFactor = spawnRate;
            // EnablePatch
            if (settings.TryGetValue($"{prefix}_BannerImageBase64", out value))
                BannerImageBase64 = value;
        }

        public Dictionary<string, string> GetSettings()
        {
            string prefix = Server.Medius.Program.Database.GetUsername();
            return new Dictionary<string, string>()
            {
                { $"{prefix}_EnablePatch", EnablePatch.ToString() },
                { $"{prefix}_EnableUnpatch", EnableUnpatch.ToString() },
                { $"{prefix}_PatchOverrideName", PatchOverrideName },
                { $"{prefix}_ScavengerHuntBeginDate", ScavengerHuntBeginDate?.ToString() },
                { $"{prefix}_ScavengerHuntEndDate", ScavengerHuntEndDate?.ToString() },
                { $"{prefix}_ScavengerHuntSpawnRateFactor", ScavengerHuntSpawnRateFactor.ToString() },
                { $"{prefix}_BannerImageBase64", BannerImageBase64 }
            };
        }
    }
}
