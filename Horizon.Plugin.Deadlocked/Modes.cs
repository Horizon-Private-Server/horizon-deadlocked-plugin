using Horizon.Plugin.Deadlocked.CustomModes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Horizon.Plugin.Deadlocked
{
    public static class Modes
    {
        static readonly BaseCustomMode[] CustomModes = new BaseCustomMode[]
        {
            new DuckHuntCustomMode(),
            new GridironCustomMode(),
            new GunGameCustomMode(),
            new HoverbikeRaceCustomMode(),
            new InfectedCustomMode(),
            new InfiniteClimberCustomMode(),
            new PayloadCustomMode(),
            new SearchAndDestroyCustomMode(),
            new SpleefCustomMode(),
            new SurvivalCustomMode(),
            new TeamDefenderCustomMode(),
            new ThousandKillsCustomMode(),
            new BenchmarkCustomMode(),
            new AnimExtractorCustomMode(),
            new TrainingCustomMode(),
            new HNSCustomMode(),
            new TagCustomMode(),
            new RaidsCustomMode(),
            new OitcCustomMode(),
            new ObstacleCustomMode(),
            new CollectathonCustomMode()
        };

        public static BaseCustomMode FindCustomModeById(CustomModeId id)
        {
            return CustomModes.FirstOrDefault(x => x.Id == id);
        }

    }


    public enum CustomModeId : sbyte
    {
        // custom map ids
        CMODE_ID_THOUSAND_KILLS = 1,
        CMODE_ID_GUN_GAME,
        CMODE_ID_INFECTED,
        CMODE_ID_PAYLOAD,
        CMODE_ID_SEARCH_AND_DESTROY,
        CMODE_ID_SURVIVAL,
        CMODE_ID_TEAM_DEFENDER,
        CMODE_ID_TRAINING,
        CMODE_ID_HNS,
        CMODE_ID_GRIDIRON,
        CMODE_ID_TAG,
        CMODE_ID_RAIDS,
        CMODE_ID_OITC,
        CMODE_ID_OBSTACLE,
        CMODE_ID_COLLECTATHON,
        CMODE_ID_ANIM_EXTRACTOR,

        // reserved for custom maps
        CMODE_ID_DUCK_HUNT = -1,
        CMODE_ID_SPLEEF = -2,
        CMODE_ID_HOVERBIKE_RACE = -3,
        CMODE_ID_INFINITE_CLIMBER = -4,
        CMODE_ID_BENCHMARK = -5,
    }

}
