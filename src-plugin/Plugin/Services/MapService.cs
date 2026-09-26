using System.Collections.Concurrent;
using K4ArenaSharedApi;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace K4Arenas;

public sealed partial class Plugin
{
    public sealed class MapServices()
    {
        public void KillServerCommandEnts()
        {
            var pointServerCommands = Core.EntitySystem.GetAllEntitiesByDesignerName<CPointServerCommand>("point_servercommand");
            foreach (var mapCmd in pointServerCommands)
            {
                if (mapCmd is { IsValid: true })
                {
                    mapCmd.AcceptInput("Kill", mapCmd, mapCmd);
                }
            }
        }

        public void ExecCustomConfig()
        {
            KillServerCommandEnts();

            var configDirectory = Path.Combine(
                Core.PluginPath,
                "..",
                "..",
                "..",
                "cfg",
                "swiftly_arenas"
            );

            var configPath = Path.Combine(
                configDirectory,
                "arena.cfg"
            );

            try
            {
                Directory.CreateDirectory(configDirectory);

                if (!File.Exists(configPath))
                {
                    File.WriteAllText(configPath, """
                // k4-arenas custom config

                bot_quota 3
                mp_autoteambalance 0
                mp_ct_default_primary ""
                mp_ct_default_secondary ""
                mp_t_default_primary ""
                mp_t_default_secondary ""
                mp_halftime 1
                mp_join_grace_time 0
                mp_match_can_clinch 0
                mp_respawn_immunitytime 0

                // Essential for better player experience
                mp_autokick 0
                mp_warmuptime 0
                mp_maxmoney 0
                mp_teamcashawards 0
                mp_playercashawards 0
                sv_disable_radar 1
                sv_ignoregrenaderadio 1

                // You can change whatever you want here, up to your preferences
                mp_endmatch_votenextmap 0
                mp_match_end_changelevel 1
                mp_match_end_restart 0
                mp_maxrounds 0
                sv_allow_votes 0
                mp_timelimit 15
                sv_talk_enemy_dead 1
                sv_talk_enemy_living 1
                sv_deadtalk 1
                """);

                    Core.Logger.LogInformation(
                        "[k4-arenas] Created config: {Path}",
                        configPath);
                }
            }
            catch (Exception error)
            {
                Core.Logger.LogError(
                    error,
                    "[k4-arenas] Failed to create config");

                return;
            }

            Core.Logger.LogInformation(
                "[k4-arenas] Executing config: swiftly_arenas/arena.cfg");

            Core.Engine.ExecuteCommand(
                "exec swiftly_arenas/arena.cfg"
            );
        }

    }
}