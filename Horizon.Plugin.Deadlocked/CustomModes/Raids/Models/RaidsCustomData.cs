using Server.Common.Stream;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    public class RaidsCustomData : ICustomGameData
    {
        public int Version { get; set; }
        public int[] Kills { get; set; }
        public int[] Revives { get; set; }
        public int[] TimesRevived { get; set; }

        public void Deserialize(MessageReader reader)
        {
            // parse by version
            Version = reader.ReadInt32();

            switch (Version)
            {
                case 1:
                    {
                        Kills = reader.ReadArray<int>(10);
                        Revives = reader.ReadArray<int>(10);
                        TimesRevived = reader.ReadArray<int>(10);
                        break;
                    }
                default:
                    {
                        Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"Unsupported raids data version {Version}");
                        break;
                    }
            }
        }
    }
}
