using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Plugin.Deadlocked
{
    public static class Stats
    {
        static readonly float[] DEFAULT_REWARD_CURVE = new float[] { 0.4f, 0.3f, 0.3f };

        public static void RateGame(IEnumerable<StatsGamePlayer> players, double[] teamScores, float teamWager = 0.3f, float ffaWager = 0.1f, float rewardFlatness = 0.5f, float drawPenalty = 0.1f)
        {
            // compute size of each team
            var teamCounts = new int[10];
            foreach (var player in players)
                teamCounts[player.Team]++;

            // determine if FFA
            if (teamCounts.Max() <= 1)
            {
                RateFfaGame(players, ffaWager, DEFAULT_REWARD_CURVE);
            }
            else
            {
                // determine winning teams
                var bestScore = teamScores.Max();
                var winningTeams = new List<int>();

                for (int i = 0; i < teamScores.Length; ++i)
                {
                    if (teamScores[i] == bestScore)
                        winningTeams.Add(i);
                }

                RateTeamGame(players, winningTeams, teamWager, rewardFlatness, drawPenalty);
            }
        }

        public static void RateTeamGame(IEnumerable<StatsGamePlayer> players, IEnumerable<int> winningTeams, float wager, float rewardFlatness, float drawPenalty)
        {
            var playerWagers = players.ToDictionary(x => x.AccountId, x => 0);
            var teamCounts = new int[10];
            var teamWagers = new int[10];
            var pool = 0;

            var isDraw = winningTeams == null || winningTeams.Count() != 1;
            Func<float, float, float, float> lerp = (a, b, t) => (b - a) * t + a;

            // generate pool from participants
            foreach (var player in players)
            {
                // force rating [100, 10000]
                player.Rank = Math.Max(100, Math.Min(10000, player.Rank));

                // compute wager
                var playerWager = (int)(player.Rank * wager);
                playerWagers[player.AccountId] = playerWager;
                pool += playerWager;
                teamWagers[player.Team] += playerWager;
                teamCounts[player.Team] += 1;

                // remove wager from player rank
                player.Rank -= playerWager;
                player.Won = !isDraw && winningTeams.Contains(player.Team) && !player.Left;
            }

            //
            var winnerTakings = (winningTeams == null || winningTeams.Count() == 0) ? 1.0f : (1f / winningTeams.Count());

            // distribute points to winners
            // handle introducing new points into system
            foreach (var player in players)
            {
                if (player.Left)
                    continue;

                // compute contribution weight as a linear combination of
                // how much player bet compared to the entire teams bet
                // and how many players in team
                var contributionWeight = lerp(playerWagers[player.AccountId] / (float)teamWagers[player.Team], 1f / teamCounts[player.Team], rewardFlatness);

                // compute player's winnings
                var winnings = pool * contributionWeight * winnerTakings;

                if (isDraw)
                {
                    // no winnings in draws
                    // player only received *back* at most the amount they contributed
                    winnings = lerp(playerWagers[player.AccountId], 0, drawPenalty);
                }
                else if (!winningTeams.Contains(player.Team))
                {
                    winnings = 0;
                }

                // add winnings to rank
                var newRating = player.Rank + (int)winnings;

                // give extra points each round
                // more points for low level
                newRating += (int)(Math.Max(0, 8 - Math.Log(newRating)) * 50 * wager);

                // finalize new rank and clamp to [100, 10000]
                player.Rank = Math.Max(100, Math.Min(10000, newRating));
            }
        }

        public static void RateFfaGame(IEnumerable<StatsGamePlayer> players, float wager, float[] rewardCurve)
        {
            var playerWagers = players.ToDictionary(x => x.AccountId, x => 0);
            var pool = 0;
            var counter = 0;
            StatsGamePlayer lastPlayer = null;

            // sort players by highest to lowest score
            players = players.OrderBy(x=>x.Left ? 1 : 0).ThenByDescending(x => x.Score).ToArray();

            foreach (var player in players)
            {
                // determine placement
                // players tied for first both get same reward
                // next best player gets third place
                counter++;

                if (lastPlayer != null && lastPlayer.Score == player.Score)
                {
                    // same placement as previous
                    player.Placement = lastPlayer.Placement;
                    player.Reward = lastPlayer.Reward;
                }
                else
                {
                    player.Placement = counter;
                    player.Reward = (counter <= rewardCurve.Length) ? rewardCurve[counter - 1] : 0f;
                }
                lastPlayer = player;

                // mark as winner
                player.Won = player.Placement == 1 && !player.Left;

                // force rating [100, 10000]
                player.Rank = Math.Max(100, Math.Min(10000, player.Rank));

                // contribute to pool
                var playerWager = (int)(player.Rank * wager);
                playerWagers[player.AccountId] = playerWager;
                player.Rank -= playerWager;
                pool += playerWager;
            }

            // compute rewards sum for normalization
            var totalRewardsPercentage = players.Sum(x => x.Reward);

            // distribute points to winners
            // handle introducing new points into system
            foreach (var player in players)
            {
                if (player.Left)
                    continue;

                var rating = player.Rank;

                // if winnings left in pool and player won something
                if (pool > 0 && player.Reward > 0)
                {
                    // get as much of the player's wager back from pool first
                    var winnings = playerWagers[player.AccountId];
                    if (winnings > pool)
                        winnings = pool;
                    pool -= winnings;

                    // get reward from pool
                    var reward = (int)((pool * player.Reward) / totalRewardsPercentage);
                    totalRewardsPercentage -= player.Reward;
                    pool -= reward;

                    // add to rating
                    rating += winnings + reward;
                }

                // give extra points each round
                // more points for low level
                rating += (int)(Math.Max(0, 8 - Math.Log(rating)) * 50 * wager);

                // finalize new rank and clamp to [100, 10000]
                player.Rank = Math.Max(100, Math.Min(10000, rating));
            }
        }
    }

    public class StatsGamePlayer
    {
        public int Index { get; set; }
        public int Team { get; set; }
        public double Score { get; set; }
        public int Rank { get; set; }
        public int AccountId { get; set; }
        public bool Left { get; set; }

        public bool Won { get; set; }
        public int Placement { get; set; }
        public float Reward { get; set; }
    }

    public enum PlayerStatIds : int
    {
        STAT_OVERALL_RANK = 2,
        STAT_WINS = 3,
        STAT_LOSSES = 4,
        STAT_DISCONNECTS = 5,
        STAT_KILLS = 6,
        STAT_DEATHS = 7,
        STAT_GAMES_PLAYED = 8,
        STAT_DEATHMATCH_RANK = 11,
        STAT_DEATHMATCH_WINS = 12,
        STAT_DEATHMATCH_LOSSES = 13,
        STAT_DEATHMATCH_KILLS = 15,
        STAT_DEATHMATCH_DEATHS = 16,
        STAT_CONQUEST_RANK = 17,
        STAT_CONQUEST_WINS = 18,
        STAT_CONQUEST_LOSSES = 19,
        STAT_CONQUEST_KILLS = 21,
        STAT_CONQUEST_DEATHS = 22,
        STAT_CONQUEST_NODES_TAKEN = 23,
        STAT_CTF_RANK = 24,
        STAT_CTF_WINS = 25,
        STAT_CTF_LOSSES = 26,
        STAT_CTF_KILLS = 28,
        STAT_CTF_DEATHS = 29,
        STAT_CTF_FLAGS_CAPTURED = 30,
        STAT_KOTH_RANK = 31,
        STAT_KOTH_WINS = 32,
        STAT_KOTH_LOSSES = 33,
        STAT_KOTH_KILLS = 35,
        STAT_KOTH_DEATHS = 36,
        STAT_KOTH_TIME = 37,
        STAT_JUGGERNAUT_RANK = 38,
        STAT_JUGGERNAUT_WINS = 39,
        STAT_JUGGERNAUT_LOSSES = 40,
        STAT_JUGGERNAUT_KILLS = 42,
        STAT_JUGGERNAUT_DEATHS = 43,
        STAT_JUGGERNAUT_TIME = 44,
        STAT_DUAL_VIPERS_KILLS = 48,
        STAT_DUAL_VIPERS_DEATHS = 49,
        STAT_MAGMA_CANNON_KILLS = 51,
        STAT_MAGMA_CANNON_DEATHS = 52,
        STAT_ARBITER_KILLS = 54,
        STAT_ARBITER_DEATHS = 55,
        STAT_FUSION_RIFLE_KILLS = 57,
        STAT_FUSION_RIFLE_DEATHS = 58,
        STAT_HUNTER_MINE_KILLS = 60,
        STAT_HUNTER_MINE_DEATHS = 61,
        STAT_B6_KILLS = 63,
        STAT_B6_DEATHS = 64,
        STAT_FLAIL_KILLS = 66,
        STAT_FLAIL_DEATHS = 67,
        STAT_ROADKILLS = 70,
        STAT_VEHICLE_SQUATS = 71,
        STAT_SQUATS = 72,
        STAT_HOLOSHIELD_KILLS = 73,
        STAT_HOLOSHIELD_DEATHS = 74,
    }

    public enum CustomPlayerStatIds : int
    {
        CUSTOM_STAT_SND_RANK = 50,
        CUSTOM_STAT_SND_WINS = 51,
        CUSTOM_STAT_SND_LOSSES = 52,
        CUSTOM_STAT_SND_GAMES_PLAYED = 53,
        CUSTOM_STAT_SND_KILLS = 54,
        CUSTOM_STAT_SND_DEATHS = 55,
        CUSTOM_STAT_SND_PLANTS = 56,
        CUSTOM_STAT_SND_DEFUSES = 57,
        CUSTOM_STAT_SND_NINJA_DEFUSES = 58,
        CUSTOM_STAT_SND_WINS_ATTACKING = 59,
        CUSTOM_STAT_SND_WINS_DEFENDING = 60,
        CUSTOM_STAT_SND_TIME_PLAYED = 61,
        CUSTOM_STAT_PAYLOAD_RANK = 70,
        CUSTOM_STAT_PAYLOAD_WINS = 71,
        CUSTOM_STAT_PAYLOAD_LOSSES = 72,
        CUSTOM_STAT_PAYLOAD_GAMES_PLAYED = 73,
        CUSTOM_STAT_PAYLOAD_KILLS = 74,
        CUSTOM_STAT_PAYLOAD_DEATHS = 75,
        CUSTOM_STAT_PAYLOAD_POINTS = 76,
        CUSTOM_STAT_PAYLOAD_KILLS_WHILE_HOT = 77,
        CUSTOM_STAT_PAYLOAD_KILLS_ON_HOT = 78,
        CUSTOM_STAT_PAYLOAD_TIME_PLAYED = 79,
        CUSTOM_STAT_SPLEEF_RANK = 90,
        CUSTOM_STAT_SPLEEF_WINS = 91,
        CUSTOM_STAT_SPLEEF_LOSSES = 92,
        CUSTOM_STAT_SPLEEF_GAMES_PLAYED = 93,
        CUSTOM_STAT_SPLEEF_ROUNDS_PLAYED = 94,
        CUSTOM_STAT_SPLEEF_POINTS = 95,
        CUSTOM_STAT_SPLEEF_TIME_PLAYED = 96,
        CUSTOM_STAT_SPLEEF_BOXES_BROKEN = 97,
        CUSTOM_STAT_INFECTED_RANK = 110,
        CUSTOM_STAT_INFECTED_WINS = 111,
        CUSTOM_STAT_INFECTED_LOSSES = 112,
        CUSTOM_STAT_INFECTED_GAMES_PLAYED = 113,
        CUSTOM_STAT_INFECTED_KILLS = 114,
        CUSTOM_STAT_INFECTED_DEATHS = 115,
        CUSTOM_STAT_INFECTED_INFECTIONS = 116,
        CUSTOM_STAT_INFECTED_TIMES_INFECTED = 117,
        CUSTOM_STAT_INFECTED_TIME_PLAYED = 118,
        CUSTOM_STAT_INFECTED_WINS_AS_SURVIVOR = 119,
        CUSTOM_STAT_INFECTED_WINS_AS_FIRST_INFECTED = 120,
        CUSTOM_STAT_GUNGAME_RANK = 130,
        CUSTOM_STAT_GUNGAME_WINS = 131,
        CUSTOM_STAT_GUNGAME_LOSSES = 132,
        CUSTOM_STAT_GUNGAME_GAMES_PLAYED = 133,
        CUSTOM_STAT_GUNGAME_KILLS = 134,
        CUSTOM_STAT_GUNGAME_DEATHS = 135,
        CUSTOM_STAT_GUNGAME_DEMOTIONS = 136,
        CUSTOM_STAT_GUNGAME_TIMES_DEMOTED = 137,
        CUSTOM_STAT_GUNGAME_TIMES_PROMOTED = 138,
        CUSTOM_STAT_GUNGAME_TIME_PLAYED = 139,
        CUSTOM_STAT_CLIMBER_RANK = 150,
        CUSTOM_STAT_CLIMBER_WINS = 151,
        CUSTOM_STAT_CLIMBER_LOSSES = 152,
        CUSTOM_STAT_CLIMBER_GAMES_PLAYED = 153,
        CUSTOM_STAT_CLIMBER_HIGH_SCORE = 154,
        CUSTOM_STAT_CLIMBER_TIME_PLAYED = 155,
        CUSTOM_STAT_SURVIVAL_RANK = 170,
        CUSTOM_STAT_SURVIVAL_GAMES_PLAYED = 171,
        CUSTOM_STAT_SURVIVAL_TIME_PLAYED = 172,
        CUSTOM_STAT_SURVIVAL_KILLS = 173,
        CUSTOM_STAT_SURVIVAL_DEATHS = 174,
        CUSTOM_STAT_SURVIVAL_REVIVES = 175,
        CUSTOM_STAT_SURVIVAL_TIMES_REVIVED = 176,
        CUSTOM_STAT_SURVIVAL_D1_HIGH_SCORE = 177,
        CUSTOM_STAT_SURVIVAL_D2_HIGH_SCORE = 178,
        CUSTOM_STAT_SURVIVAL_D3_HIGH_SCORE = 179,
        CUSTOM_STAT_SURVIVAL_D4_HIGH_SCORE = 180,
        CUSTOM_STAT_SURVIVAL_D5_HIGH_SCORE = 181,
        CUSTOM_STAT_SURVIVAL_WRENCH_KILLS = 182,
        CUSTOM_STAT_SURVIVAL_DUAL_VIPER_KILLS = 183,
        CUSTOM_STAT_SURVIVAL_MAGMA_CANNON_KILLS = 184,
        CUSTOM_STAT_SURVIVAL_ARBITER_KILLS = 185,
        CUSTOM_STAT_SURVIVAL_FUSION_RIFLE_KILLS = 186,
        CUSTOM_STAT_SURVIVAL_MINE_LAUNCHER_KILLS = 187,
        CUSTOM_STAT_SURVIVAL_B6_OBLITERATOR_KILLS = 188,
        CUSTOM_STAT_SURVIVAL_SCORPION_FLAIL_KILLS = 189,
        CUSTOM_STAT_SURVIVAL_XP = 190,
        CUSTOM_STAT_TRAINING_RANK = 210,
        CUSTOM_STAT_TRAINING_GAMES_PLAYED = 211,
        CUSTOM_STAT_TRAINING_TIME_PLAYED = 212,
        CUSTOM_STAT_TRAINING_TOTAL_KILLS = 213,
        CUSTOM_STAT_TRAINING_FUSION_BEST_POINTS = 214,
        CUSTOM_STAT_TRAINING_FUSION_BEST_TIME = 215,
        CUSTOM_STAT_TRAINING_FUSION_KILLS = 216,
        CUSTOM_STAT_TRAINING_FUSION_HITS = 217,
        CUSTOM_STAT_TRAINING_FUSION_MISSES = 218,
        CUSTOM_STAT_TRAINING_FUSION_ACCURACY = 219,
        CUSTOM_STAT_TRAINING_FUSION_BEST_COMBO = 220,
        CUSTOM_STAT_TRAINING_CYCLE_BEST_POINTS = 221,
        CUSTOM_STAT_TRAINING_CYCLE_BEST_COMBO = 222,
        CUSTOM_STAT_TRAINING_CYCLE_KILLS = 223,
        CUSTOM_STAT_TRAINING_CYCLE_DEATHS = 224,
        CUSTOM_STAT_TRAINING_CYCLE_FUSION_HITS = 225,
        CUSTOM_STAT_TRAINING_CYCLE_FUSION_MISSES = 226,
        CUSTOM_STAT_TRAINING_CYCLE_FUSION_ACCURACY = 227,
    }
}
