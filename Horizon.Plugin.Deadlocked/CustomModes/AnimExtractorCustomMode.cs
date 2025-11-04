using Horizon.Plugin.Deadlocked.Messages;
using Server.Common.Stream;
using Server.Medius.Models;
using Server.Medius.PluginArgs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked.CustomModes
{
    public class AnimExtractorCustomMode : BaseCustomMode
    {
        public class SequenceJson
        {
            public int OClass;
            public int SeqId;
            public float Scale;
            public List<SequenceFrameJson> Frames;
        }

        public class SequenceFrameJson
        {
            public float Time;
            public List<float[]> Matricies = new List<float[]>();
        }

        public override CustomModeId Id => CustomModeId.CMODE_ID_ANIM_EXTRACTOR;
        public override string Name => "Anim Extractor";

        public override Task<int?> GetRank(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            return Task.FromResult((int?)null);
        }

        public override Task OnClientPostWideStats(OnPlayerWideStatsArgs args)
        {
            // accept all
            // since this is just deathmatch with a large kill limit
            return Task.CompletedTask;
        }

        public override Task<string> GetGameInfo(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            return Task.FromResult(string.Empty);
        }

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata, ClientObject client)
        {
            return Task.FromResult(new Payload(0x000F0000, File.ReadAllBytes(Path.Combine(Plugin.WorkingDirectory, "bin/patch/anim-extractor-11184.bin"))));
        }

        protected override ICustomGameData CreateCustomGameData()
        {
            return null;
        }

        protected override bool GameAcceptStats(Server.Medius.Models.Game game, GameMetadata metadata, GameData gameData)
        {
            // we don't track stats for this mode
            return false;
        }

        protected override Task UpdateCustomStats(CustomModeUpdateStatsArgs args)
        {
            throw new NotImplementedException();
        }

        Dictionary<(short, short), SequenceJson> activeSequences = new Dictionary<(short, short), SequenceJson>();
        public override Task OnRecvCustomMessage(ClientObject client, int messageId, MessageReader reader)
        {
            if (messageId == 100)
            {
                AnimExtractorJointCacheBlockMessage msg = new AnimExtractorJointCacheBlockMessage();
                msg.Deserialize(reader);

                Console.WriteLine($"{msg.Time}: {msg.Offset / 0x40}");

                // find existing sequence if it exists
                var key = (msg.OClass, msg.SeqId);
                if (!activeSequences.TryGetValue(key, out var sequence))
                    activeSequences.Add(key, sequence = new SequenceJson() { OClass = msg.OClass, SeqId = msg.SeqId, Scale = msg.Scale, Frames = new List<SequenceFrameJson>() });

                // find existing time if it exists
                var frame = sequence.Frames.FirstOrDefault(x => x.Time == msg.Time);
                if (frame == null)
                    sequence.Frames.Add(frame = new SequenceFrameJson() { Time = msg.Time, Matricies = CreateDefaultMatricies(msg.JointCount) });

                // update matricies
                var index = msg.Offset / 0x40;
                for (int m = 0; m < 7; ++m)
                {
                    if ((m + index) < msg.JointCount)
                    {
                        frame.Matricies[m + index] = msg.Data.Skip(16 * m).Take(16).ToArray();
                    }
                }

                // 
                if (msg.IsEnd)
                {
                    client.Queue(new AnimExtractorJointCacheBlockConfirmationMessage());
                }
            }
            else if (messageId == 101)
            {
                AnimExtractorJointCacheCompleteMessage msg = new AnimExtractorJointCacheCompleteMessage();
                msg.Deserialize(reader);

                // find existing sequence if it exists
                var key = (msg.OClass, msg.SeqId);
                if (activeSequences.TryGetValue(key, out var sequence))
                {
                    // save to file
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(sequence);
                    var dirPath = $"M:\\VS\\asd\\wrench\\games\\dl_scus_974_65\\mobies\\{msg.OClass}\\";
                    if (!Directory.Exists(dirPath))
                    {
                        // moby doesn't exist yet
                        return Task.CompletedTask;
                    }

                    dirPath = Path.Combine(dirPath, "anims");
                    if (!Directory.Exists(dirPath))
                        Directory.CreateDirectory(dirPath);
                    var path = Path.Combine(dirPath, $"mesh_{msg.SeqId}.json");
                    File.WriteAllText(path, json);

                    // remove from sequences
                    activeSequences.Remove(key);
                }
            }

            return Task.CompletedTask;
        }

        private List<float[]> CreateDefaultMatricies(int count)
        {
            float[] defaultMatrix = new float[]
            {
                1,0,0,0,
                0,1,0,0,
                0,0,1,0,
                0,0,0,1,
            };

            return Enumerable.Range(0, count).Select(x => defaultMatrix.ToArray()).ToList();
        }
    }
}
