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
    public class SearchAndDestroyCustomMode : BaseCustomMode
    {
        private static readonly Random _rng = new Random();
        private static readonly SNDConfig[] _configs = new SNDConfig[]
        {
            new SNDConfig(41) // Battledome
            {
                DefendSpawnPoint = new float[] { 870.87f, 557.89f, 540.07f, -03.14f },
                AttackSpawnPoint = new float[] { 797.86f, 557.83f, 500.50f, 000.00f },
                Node1SpawnPoint  = new float[] { 845.40f, 558.24f, 540.00f, 000.00f },
                Node2SpawnPoint  = new float[] { 845.40f, 558.24f, 519.50f, 000.00f },
                PackSpawnPoint   = new float[] { 804.55f, 558.19f, 501.40f, 000.00f }
            },
            new SNDConfig(42) // Catacrom
            {
                DefendSpawnPoint = new float[] { 250.10f, 378.74f, 068.55f, 000.00f },
                AttackSpawnPoint = new float[] { 461.38f, 251.86f, 068.07f, 003.11f },
                Node1SpawnPoint  = new float[] { 363.08f, 312.48f, 067.77f, 000.00f },
                Node2SpawnPoint  = new float[] { 270.77f, 325.58f, 062.50f, 000.00f },
                PackSpawnPoint   = new float[] { 395.05f, 220.10f, 068.10f, 000.00f }
            },
            new SNDConfig(44) // Sarathos
            {
                DefendSpawnPoint  = new float[] { 268.386f, 122.752f, 103.479f, 0.800f },
                AttackSpawnPoint  = new float[] { 519.269f, 396.575f, 106.727f, -1.351f },
                Node1SpawnPoint   = new float[] { 428.368f, 239.646f, 106.613f, 0.000f },
                Node2SpawnPoint   = new float[] { 411.456f, 143.924f, 105.344f, 0.000f },
                PackSpawnPoint    = new float[] { 526.056f, 370.259f, 107.271f, 0.000f },
            },
            new SNDConfig(45) // Dark Cathedral
            {
                DefendSpawnPoint  = new float[] { 349.370f, 529.600f, 641.250f, 0.000f },
                AttackSpawnPoint  = new float[] { 659.060f, 447.520f, 651.170f, -3.140f },
                Node1SpawnPoint   = new float[] { 503.700f, 494.400f, 641.300f, 0.000f },
                Node2SpawnPoint   = new float[] { 443.020f, 415.420f, 641.280f, 0.000f },
                PackSpawnPoint    = new float[] { 646.540f, 447.520f, 651.170f, 0.000f },
            },
            new SNDConfig(46) // Shaar
            {
                DefendSpawnPoint  = new float[] { 544.610f, 548.150f, 509.310f, 1.570f },
                AttackSpawnPoint  = new float[] { 638.050f, 688.370f, 515.560f, -1.570f },
                Node1SpawnPoint   = new float[] { 544.660f, 624.500f, 521.340f, 0.000f },
                Node2SpawnPoint   = new float[] { 459.680f, 622.620f, 515.550f, 0.000f },
                PackSpawnPoint    = new float[] { 635.300f, 673.050f, 515.470f, 0.000f },
            },
            new SNDConfig(47) // Valix
            {
                DefendSpawnPoint  = new float[] { 400.980f, 416.240f, 325.190f, -2.570f },
                AttackSpawnPoint  = new float[] { 375.300f, 657.210f, 328.770f, -2.110f },
                Node1SpawnPoint   = new float[] { 335.380f, 428.380f, 328.960f, 0.000f },
                Node2SpawnPoint   = new float[] { 310.060f, 542.830f, 328.580f, 0.000f },
                PackSpawnPoint    = new float[] { 301.320f, 486.950f, 330.970f, 0.000f },
            },
            new SNDConfig(48) // Mining Facility
            {
                DefendSpawnPoint  = new float[] { 459.950f, 599.320f, 435.000f, 3.140f },
                AttackSpawnPoint  = new float[] { 358.640f, 600.640f, 428.400f, 0.000f },
                Node1SpawnPoint   = new float[] { 411.370f, 557.560f, 428.100f, 0.000f },
                Node2SpawnPoint   = new float[] { 410.240f, 662.650f, 428.000f, 0.000f },
                PackSpawnPoint    = new float[] { 369.120f, 601.280f, 428.030f, 0.000f },
            },
            new SNDConfig(50) // Torval
            {
                DefendSpawnPoint  = new float[] { 231.230f, 277.800f, 106.950f, 0.000f },
                AttackSpawnPoint  = new float[] { 396.140f, 319.300f, 101.030f, -3.140f },
                Node1SpawnPoint   = new float[] { 244.850f, 401.110f, 100.820f, 0.000f },
                Node2SpawnPoint   = new float[] { 302.000f, 269.170f, 115.080f, 0.000f },
                PackSpawnPoint    = new float[] { 388.240f, 318.150f, 102.000f, 0.000f },
            },
            new SNDConfig(51) // Tempus
            {
                DefendSpawnPoint  = new float[] { 436.440f, 347.980f, 122.590f, 0.870f },
                AttackSpawnPoint  = new float[] { 465.250f, 591.440f, 102.890f, -0.530f },
                Node1SpawnPoint   = new float[] { 408.840f, 440.960f, 100.830f, 0.000f },
                Node2SpawnPoint   = new float[] { 527.930f, 475.010f, 122.380f, 0.000f },
                PackSpawnPoint    = new float[] { 478.770f, 583.800f, 103.000f, 0.000f },
            },
            new SNDConfig(53) // Maraxus
            {
                DefendSpawnPoint  = new float[] { 390.920f, 690.330f, 106.200f, 0.780f },
                AttackSpawnPoint  = new float[] { 589.590f, 701.020f, 102.240f, 2.440f },
                Node1SpawnPoint   = new float[] { 485.980f, 707.050f, 108.800f, 0.000f },
                Node2SpawnPoint   = new float[] { 445.240f, 637.080f, 102.550f, 0.000f },
                PackSpawnPoint    = new float[] { 569.630f, 718.320f, 103.000f, 0.000f },
            },
            new SNDConfig(54) // Ghost Station
            {
                DefendSpawnPoint  = new float[] { 637.320f, 700.880f, 100.000f, -1.570f },
                AttackSpawnPoint  = new float[] { 638.350f, 323.550f, 102.650f, 1.570f },
                Node1SpawnPoint   = new float[] { 499.040f, 567.080f, 102.830f, 0.000f },
                Node2SpawnPoint   = new float[] { 729.700f, 564.980f, 101.580f, 0.000f },
                PackSpawnPoint    = new float[] { 637.640f, 358.010f, 102.650f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_ANNIHILATION_NATION) // Annihilation nation
            {
                DefendSpawnPoint  = new float[] { 472.008f, 489.567f, 432.281f, -0.573f },
                AttackSpawnPoint  = new float[] { 462.049f, 244.455f, 426.050f, 1.595f },
                Node1SpawnPoint   = new float[] { 501.018f, 391.978f, 429.453f, 0.000f },
                Node2SpawnPoint   = new float[] { 436.030f, 424.331f, 432.344f, 0.000f },
                PackSpawnPoint    = new float[] { 461.757f, 278.486f, 425.778f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_BAKISI_ISLES) // Bakisi
            {
                DefendSpawnPoint  = new float[] { 376.523f, 574.788f, 200.000f, -1.442f },
                AttackSpawnPoint  = new float[] { 419.824f, 255.069f, 231.656f, 1.695f },
                Node1SpawnPoint   = new float[] { 503.978f, 459.699f, 200.281f, 0.000f },
                Node2SpawnPoint   = new float[] { 282.378f, 424.257f, 200.281f, 0.000f },
                PackSpawnPoint    = new float[] { 418.727f, 265.168f, 230.688f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_BDOME_SP) // Battledome SP
            {
                DefendSpawnPoint  = new float[] { 748.610f, 655.027f, 519.344f, 2.327f },
                AttackSpawnPoint  = new float[] { 880.352f, 524.907f, 500.361f, 2.372f },
                Node1SpawnPoint   = new float[] { 716.010f, 557.730f, 500.500f, 0.000f },
                Node2SpawnPoint   = new float[] { 684.981f, 780.414f, 500.361f, 0.000f },
                PackSpawnPoint    = new float[] { 869.790f, 531.416f, 500.400f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_BLACKWATER_CITY) // Blackwater city
            {
                DefendSpawnPoint  = new float[] { 219.389f, 385.107f, 82.594f, -1.571f },
                AttackSpawnPoint  = new float[] { 219.389f, 148.362f, 82.641f, 1.571f },
                Node1SpawnPoint   = new float[] { 177.960f, 348.583f, 97.291f, 0.000f },
                Node2SpawnPoint   = new float[] { 261.390f, 348.583f, 97.291f, 0.000f },
                PackSpawnPoint    = new float[] { 219.930f, 171.276f, 80.600f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_BLACKWATER_DOCKS) // Blackwater docks
            {
                DefendSpawnPoint  = new float[] { 195.440f, 272.071f, 105.250f, -0.633f },
                AttackSpawnPoint  = new float[] { 268.655f, 185.356f, 99.672f, 2.421f },
                Node1SpawnPoint   = new float[] { 212.022f, 191.593f, 99.281f, 0.000f },
                Node2SpawnPoint   = new float[] { 271.568f, 264.302f, 97.250f, 0.000f },
                PackSpawnPoint    = new float[] { 263.924f, 189.667f, 99.729f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_CONTAINMENT_SUITE) // Containment suite
            {
                DefendSpawnPoint  = new float[] { 202.610f, 480.593f, 125.844f, -0.976f },
                AttackSpawnPoint  = new float[] { 331.222f, 424.861f, 125.156f, 3.141f },
                Node1SpawnPoint   = new float[] { 246.262f, 426.687f, 125.969f, 0.000f },
                Node2SpawnPoint   = new float[] { 219.610f, 378.513f, 125.969f, 0.000f },
                PackSpawnPoint    = new float[] { 315.092f, 424.973f, 125.200f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_DC_INTERIOR) // Containment suite
            {
                DefendSpawnPoint  = new float[] { 202.610f, 480.593f, 125.844f, -0.976f },
                AttackSpawnPoint  = new float[] { 331.222f, 424.861f, 125.156f, 3.141f },
                Node1SpawnPoint   = new float[] { 246.262f, 426.687f, 125.969f, 0.000f },
                Node2SpawnPoint   = new float[] { 219.610f, 378.513f, 125.969f, 0.000f },
                PackSpawnPoint    = new float[] { 315.092f, 424.973f, 125.200f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_DESERT_PRISON) // Desert prison
            {
                DefendSpawnPoint  = new float[] { 606.760f, 654.048f, 102.450f, -2.821f },
                AttackSpawnPoint  = new float[] { 453.420f, 596.346f, 102.153f, 0.530f },
                Node1SpawnPoint   = new float[] { 486.043f, 707.050f, 108.797f, 0.000f },
                Node2SpawnPoint   = new float[] { 537.053f, 499.883f, 101.109f, 0.000f },
                PackSpawnPoint    = new float[] { 464.699f, 602.947f, 101.001f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_GHOST_SHIP) // Ghost ship
            {
                DefendSpawnPoint  = new float[] { 73.775f, 876.340f, 360.328f, 0.000f },
                AttackSpawnPoint  = new float[] { 273.307f, 875.010f, 364.000f, 3.141f },
                Node1SpawnPoint   = new float[] { 158.848f, 702.583f, 382.156f, 0.000f },
                Node2SpawnPoint   = new float[] { 158.845f, 954.512f, 363.672f, 0.000f },
                PackSpawnPoint    = new float[] { 258.772f, 874.934f, 364.191f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_HOVEN_GORGE) // Hoven
            {
                DefendSpawnPoint  = new float[] { 357.103f, 353.035f, 67.781f, -2.279f },
                AttackSpawnPoint  = new float[] { 164.648f, 257.566f, 66.158f, 0.062f },
                Node1SpawnPoint   = new float[] { 255.952f, 332.026f, 78.516f, 0.000f },
                Node2SpawnPoint   = new float[] { 290.515f, 207.614f, 73.390f, 0.000f },
                PackSpawnPoint    = new float[] { 177.378f, 258.349f, 65.944f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_KORGON_OUTPOST) // Korgon outpost
            {
                DefendSpawnPoint  = new float[] { 203.545f, 344.146f, 97.734f, -0.166f },
                AttackSpawnPoint  = new float[] { 442.846f, 343.799f, 97.734f, -2.909f },
                Node1SpawnPoint   = new float[] { 322.487f, 408.496f, 96.016f, 0.000f },
                Node2SpawnPoint   = new float[] { 300.532f, 233.370f, 96.016f, 0.000f },
                PackSpawnPoint    = new float[] { 431.093f, 338.332f, 96.020f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_LAUNCH_SITE) // Launch site
            {
                DefendSpawnPoint  = new float[] { 147.411f, 291.763f, 202.500f, -0.218f },
                AttackSpawnPoint  = new float[] { 385.836f, 180.145f, 201.859f, 2.386f },
                Node1SpawnPoint   = new float[] { 191.903f, 178.414f, 201.879f, 0.000f },
                Node2SpawnPoint   = new float[] { 306.137f, 382.373f, 201.828f, 0.000f },
                PackSpawnPoint    = new float[] { 378.781f, 187.813f, 201.866f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_MARCADIA_PALACE) // Marcadia palace
            {
                DefendSpawnPoint  = new float[] { 419.010f, 843.821f, 115.050f, 0.512f },
                AttackSpawnPoint  = new float[] { 538.153f, 843.369f, 115.015f, 2.590f },
                Node1SpawnPoint   = new float[] { 478.400f, 888.289f, 118.656f, 0.000f },
                Node2SpawnPoint   = new float[] { 0.000f, 0.000f, 0.000f, 0.000f },
                PackSpawnPoint    = new float[] { 531.523f, 847.596f, 115.828f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_METROPOLIS_MP) // Metropolis
            {
                DefendSpawnPoint  = new float[] { 775.194f, 456.652f, 337.265f, -1.468f },
                AttackSpawnPoint  = new float[] { 735.455f, 221.783f, 337.265f, 1.616f },
                Node1SpawnPoint   = new float[] { 649.448f, 380.975f, 362.393f, 0.000f },
                Node2SpawnPoint   = new float[] { 861.380f, 297.120f, 362.393f, 0.000f },
                PackSpawnPoint    = new float[] { 734.887f, 234.810f, 335.359f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_MF_SP) // Mining Facility SP
            {
                DefendSpawnPoint  = new float[] { 332.275f, 545.239f, 434.000f, -0.012f },
                AttackSpawnPoint  = new float[] { 616.914f, 654.052f, 427.344f, 3.117f },
                Node1SpawnPoint   = new float[] { 502.907f, 599.960f, 434.000f, 0.000f },
                Node2SpawnPoint   = new float[] { 523.754f, 516.618f, 427.343f, 0.000f },
                PackSpawnPoint    = new float[] { 604.955f, 654.342f, 427.344f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_SARATHOS_SP) // Sarathos SP
            {
                DefendSpawnPoint  = new float[] { 268.386f, 122.752f, 103.479f, 0.800f },
                AttackSpawnPoint  = new float[] { 519.269f, 396.575f, 106.727f, -1.351f },
                Node1SpawnPoint   = new float[] { 428.368f, 239.646f, 106.613f, 0.000f },
                Node2SpawnPoint   = new float[] { 411.456f, 143.924f, 105.344f, 0.000f },
                PackSpawnPoint    = new float[] { 526.056f, 370.259f, 107.271f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_SHAAR_SP) // Shaar SP
            {
                DefendSpawnPoint  = new float[] { 453.367f, 683.798f, 515.469f, -1.392f },
                AttackSpawnPoint  = new float[] { 629.879f, 555.042f, 509.297f, 2.599f },
                Node1SpawnPoint   = new float[] { 544.672f, 624.565f, 511.562f, 0.000f },
                Node2SpawnPoint   = new float[] { 492.130f, 449.107f, 497.871f, 0.000f },
                PackSpawnPoint    = new float[] { 617.215f, 562.665f, 509.297f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_SHIPMENT) // Shipment
            {
                DefendSpawnPoint  = new float[] { 336.198f, 264.246f, 167.275f, 2.318f },
                AttackSpawnPoint  = new float[] { 274.635f, 332.858f, 167.275f, -0.980f },
                Node1SpawnPoint   = new float[] { 278.897f, 270.440f, 167.275f, 0.000f },
                Node2SpawnPoint   = new float[] { 334.353f, 329.983f, 167.275f, 0.000f },
                PackSpawnPoint    = new float[] { 279.740f, 324.940f, 267.275f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_TORVAL_SP) // Torval SP
            {
                DefendSpawnPoint  = new float[] { 183.970f, 492.105f, 107.063f, -0.012f },
                AttackSpawnPoint  = new float[] { 352.869f, 169.917f, 112.000f, 2.057f },
                Node1SpawnPoint   = new float[] { 362.134f, 327.758f, 100.952f, 0.000f },
                Node2SpawnPoint   = new float[] { 248.652f, 343.518f, 107.000f, 0.000f },
                PackSpawnPoint    = new float[] { 348.572f, 175.970f, 112.005f, 0.000f },
            },
            new SNDConfig(CustomMapId.CMAP_ID_TYHRRANOSIS) // Tyhrranosis
            {
                DefendSpawnPoint  = new float[] { 555.673f, 669.214f, 94.000f, 0.850f },
                AttackSpawnPoint  = new float[] { 945.619f, 542.867f, 100.515f, 2.550f },
                Node1SpawnPoint   = new float[] { 773.373f, 602.050f, 100.018f, 0.000f },
                Node2SpawnPoint   = new float[] { 864.899f, 743.047f, 112.000f, 0.000f },
                PackSpawnPoint    = new float[] { 933.878f, 550.395f, 101.507f, 0.000f },
            }
        };

        public override CustomModeId Id => CustomModeId.CMODE_ID_SEARCH_AND_DESTROY;
        public override string Name => "Search and Destroy";

        public override Task<int?> GetRank(ClientObject client)
        {
            return Task.FromResult((int?)client.CustomWideStats[(int)CustomPlayerStatIds.CUSTOM_STAT_SND_RANK]);
        }

        public override Task OnClientPostWideStats(OnPlayerWideStatsArgs args)
        {
            var client = args.Player;

            args.WideStats[(int)PlayerStatIds.STAT_OVERALL_RANK] = client.WideStats[(int)PlayerStatIds.STAT_OVERALL_RANK];
            for (int i = (int)PlayerStatIds.STAT_CONQUEST_RANK; i <= (int)PlayerStatIds.STAT_CONQUEST_NODES_TAKEN; ++i)
                args.WideStats[i] = client.WideStats[i];

            return Task.CompletedTask;
        }

        public override Task<string> GetGameInfo(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            return Task.FromResult($"Round timelimit: {SNDConfig.RoundTimelimit/60.0f:0.##} minutes\nRounds to win: {SNDConfig.RoundsToWin}");
        }

        public override Task<Payload> GetPayload(Server.Medius.Models.Game game, GameMetadata metadata)
        {
            var payload = new Payload(0x000F0000, File.ReadAllBytes(Path.Combine(Plugin.WorkingDirectory, "bin/patch/snd-11184.bin")));

            // find config by custom map then by regular map
            var config = _configs.FirstOrDefault(x => x.CustomMapId == (CustomMapId)metadata.GameConfig.MapOverride);
            if (config == null)
                config = _configs.FirstOrDefault(x => x.MapId == game.GameLevel);

            // insert config into payload
            if (config != null)
            {
                using (var ms = new MemoryStream(payload.Data, true))
                {
                    using (var writer = new BinaryWriter(ms))
                    {
                        config.Serialize(writer);
                    }
                }
            }

            return Task.FromResult(payload);
        }

        protected override ICustomGameData CreateCustomGameData()
        {
            return new SNDCustomData();
        }

        protected override bool GameAcceptStats(Server.Medius.Models.Game game, GameMetadata metadata, GameData gameData)
        {
            if (game == null || metadata == null || gameData == null)
                return false;

            // need at least two players
            if (gameData.StartGameSettings.PlayerAccountIds.Count(x => x > 0) <= 1)
                return false;

            // construct team info
            var teamIds = new List<int>();
            var teamCounts = new int[10];
            foreach (var team in gameData.StartGameSettings.PlayerTeams)
            {
                if (team >= 0 && team < 10)
                {
                    teamCounts[team] += 1;
                    if (!teamIds.Contains(team))
                        teamIds.Add(team);
                }
            }

            // need exactly two teams
            if (teamIds.Count != 2)
                return false;

            // game must last at least a minute
            if (!game.UtcTimeEnded.HasValue || !game.UtcTimeStarted.HasValue || (game.UtcTimeEnded - game.UtcTimeStarted)?.TotalSeconds < 60)
                return false;

            // game must have custom data
            if (gameData.CustomGameData == null)
                return false;

            // game must have lasted at least two rounds
            if ((gameData.CustomGameData as SNDCustomData).RoundWinner.Count < 2)
                return false;

            return true;
        }

        protected override Task UpdateCustomStats(CustomModeUpdateStatsArgs args)
        {
            var customGameData = args.GameData.CustomGameData as SNDCustomData;
            var gameData = args.GameData;
            var game = args.Game;
            int[] winningTeams = null;
            int[] teamWins = new int[2];
            int[] teamWinsAttacking = new int[2];
            int[] teamWinsDefending = new int[2];

            // set rank and score of players
            foreach (var player in args.Players)
            {
                player.Rank = args.PlayerCustomStats[player.AccountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SND_RANK];
            }

            // determine number of rounds each team won
            foreach (var roundWinner in customGameData.RoundWinner)
            {
                var teamIdWin = roundWinner.AttackersWon || roundWinner.TeamsFlipped ? 1 : 0;
                teamWins[teamIdWin]++;
                if (roundWinner.AttackersWon)
                    teamWinsAttacking[teamIdWin]++;
                else
                    teamWinsDefending[teamIdWin]++;
            }

            // determine winning team
            if (teamWins[1] > teamWins[0])
            {
                winningTeams = new int[] { 1 };
            }
            else if (teamWins[0] > teamWins[1])
            {
                winningTeams = new int[] { 0 };
            }

            // rate game
            Stats.RateTeamGame(args.Players, winningTeams, wager: 0.3f, rewardFlatness: 0.5f, drawPenalty: 0.2f);

            // apply stats
            foreach (var accountId in args.PlayerCustomStats.Keys)
            {
                var player = args.Players.FirstOrDefault(x => x.AccountId == accountId);
                var gameIdx = player.Index;

                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SND_RANK] = player.Rank;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SND_KILLS] += gameData.Data.Kills[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SND_DEATHS] += gameData.Data.Deaths[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SND_GAMES_PLAYED] += 1;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SND_WINS] += player.Won ? 1 : 0;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SND_LOSSES] += player.Won ? 0 : 1;
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SND_PLANTS] += customGameData.BombsPlanted[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SND_DEFUSES] += customGameData.BombsDefused[gameIdx];
                args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SND_NINJA_DEFUSES] += customGameData.BombsNinjaDefused[gameIdx];

                if (!player.Left)
                {
                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SND_TIME_PLAYED] += (int)((game.UtcTimeEnded - game.UtcTimeStarted)?.TotalSeconds ?? 0);
                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SND_WINS_ATTACKING] += teamWinsAttacking[player.Team];
                    args.PlayerCustomStats[accountId][(int)CustomPlayerStatIds.CUSTOM_STAT_SND_WINS_DEFENDING] += teamWinsDefending[player.Team];
                }
            }

            return Task.CompletedTask;
        }
    }

    public class SNDCustomData : ICustomGameData
    {
        public class SNDCustomDataRoundState
        {
            public bool AttackersWon { get; set; }
            public bool TeamsFlipped { get; set; }
            public int? BombSitePlanted { get; set; }
        }

        public int Version { get; set; }
        public List<SNDCustomDataRoundState> RoundWinner { get; set; }
        public short[] BombsPlanted { get; set; }
        public short[] BombsDefused { get; set; }
        public short[] BombsNinjaDefused { get; set; }

        public void Deserialize(BinaryReader reader)
        {
            // parse by version
            Version = reader.ReadInt32();
            RoundWinner = new List<SNDCustomDataRoundState>();

            switch (Version)
            {
                case 1:
                    {
                        var roundWinners = reader.ReadArray<sbyte>(32);
                        BombsPlanted = new short[10];
                        BombsDefused = new short[10];
                        BombsNinjaDefused = new short[10];

                        foreach (var roundWinner in roundWinners)
                        {
                            if (roundWinner != -1)
                            {
                                RoundWinner.Add(new SNDCustomDataRoundState()
                                {
                                    AttackersWon = (roundWinner & 0x01) != 0,
                                    TeamsFlipped = (roundWinner & 0x10) != 0,
                                    BombSitePlanted = null
                                });
                            }
                        }
                        break;
                    }
                case 2:
                    {
                        var roundWinners = reader.ReadArray<sbyte>(32);
                        BombsPlanted = reader.ReadArray<short>(10);
                        BombsDefused = reader.ReadArray<short>(10);
                        BombsNinjaDefused = reader.ReadArray<short>(10);


                        foreach (var roundWinner in roundWinners)
                        {
                            if (roundWinner != -1)
                            {
                                RoundWinner.Add(new SNDCustomDataRoundState()
                                {
                                    AttackersWon = (roundWinner & 0x01) != 0,
                                    TeamsFlipped = (roundWinner & 0x10) != 0,
                                    BombSitePlanted = (roundWinner & 0x80) != 0 ? (int?)null : (roundWinner & 0x40)
                                });
                            }
                        }
                        break;
                    }
                default:
                    {
                        Plugin.Host.Log(DotNetty.Common.Internal.Logging.InternalLogLevel.WARN, $"Unsupported snd data version {Version}");
                        break;
                    }
            }
        }
    }

    public class SNDConfig
    {
        public const uint Offset = 0x20;
        public const int BombDetonationTimer = 30;
        public const int RoundsToWin  = 6;
        public const int RoundsToFlip = 3;
        public const int RoundTimelimit = 2 * 60;

        public CustomMapId? CustomMapId { get; }
        public int? MapId { get; }
        public float[] DefendSpawnPoint { get; set; }
        public float[] AttackSpawnPoint { get; set; }
        public float[] Node1SpawnPoint { get; set; }
        public float[] Node2SpawnPoint { get; set; }
        public float[] PackSpawnPoint { get; set; }

        public SNDConfig(CustomMapId mapId)
        {
            CustomMapId = mapId;
        }

        public SNDConfig(int mapId)
        {
            MapId = mapId;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.BaseStream.Seek(Offset, SeekOrigin.Begin);

            WriteVector(writer, DefendSpawnPoint);
            WriteVector(writer, AttackSpawnPoint);
            WriteVector(writer, Node1SpawnPoint);
            WriteVector(writer, Node2SpawnPoint);
            WriteVector(writer, PackSpawnPoint);
            writer.Write(BombDetonationTimer);
            writer.Write(RoundsToWin);
            writer.Write(RoundsToFlip);
            writer.Write(RoundTimelimit);
        }

        private void WriteVector(BinaryWriter writer, float[] vector)
        {
            for (int i = 0; i < 4; ++i)
            {
                if (vector == null || i >= vector.Length)
                    writer.Write(0f);
                else
                    writer.Write(vector[i]);
            }
        }
    }
}
