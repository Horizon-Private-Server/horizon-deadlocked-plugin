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
        }

        public Dictionary<string, string> GetSettings()
        {
            string prefix = Server.Medius.Program.Database.GetUsername();
            return new Dictionary<string, string>()
            {
                { $"{prefix}_EnablePatch", EnablePatch.ToString() },
                { $"{prefix}_EnableUnpatch", EnableUnpatch.ToString() },
                { $"{prefix}_PatchOverrideName", PatchOverrideName },
            };
        }
    }
}
