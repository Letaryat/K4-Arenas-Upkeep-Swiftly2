using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menu.Components;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace K4Arenas;

public partial class MenuServices
{
    private readonly ISwiftlyCore _core;
    private readonly PlayerManager _playerManager;
    private readonly ArenaManager _arenaManager;
    private readonly DatabaseService _databaseService;
    private readonly DuelService _duelService;

    public MenuServices(
        ISwiftlyCore core,
        PlayerManager playerManager,
        ArenaManager arenaManager,
        DatabaseService databaseService,
        DuelService duelService)
    {
        _core = core;
        _playerManager = playerManager;
        _arenaManager = arenaManager;
        _databaseService = databaseService;
        _duelService = duelService;
    }

    public void ShowWeaponPreferencesMenu(ArenaPlayer arenaPlayer)
    {
        var localizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

        var weaponTypes = new[]
        {
        (CSWeaponType.WEAPONTYPE_RIFLE, "k4.menu.guns.rifle"),
        (CSWeaponType.WEAPONTYPE_SNIPER_RIFLE, "k4.menu.guns.sniper"),
        (CSWeaponType.WEAPONTYPE_SHOTGUN, "k4.menu.guns.shotgun"),
        (CSWeaponType.WEAPONTYPE_SUBMACHINEGUN, "k4.menu.guns.smg"),
        (CSWeaponType.WEAPONTYPE_MACHINEGUN, "k4.menu.guns.lmg"),
        (CSWeaponType.WEAPONTYPE_PISTOL, "k4.menu.guns.pistol")
    };

        var menuBuilder = _core.Menu
            .CreateMenu("k4.guns.preferences")
            .WithTitle(localizer["k4.menu.guns.title"])
            .WithItemsPerPage(5);

        foreach (var (type, translationKey) in weaponTypes)
        {
            var capturedType = type;

            menuBuilder.Add(new ButtonComponent(
                localizer[translationKey],
                ctx =>
                {
                    ShowWeaponSelectionMenu(arenaPlayer, capturedType);
                    return ValueTask.CompletedTask;
                }
            ));
        }

        var menu = menuBuilder.Build();
        menu.Open(arenaPlayer.Player);
    }
    private void ShowWeaponSelectionMenu(ArenaPlayer arenaPlayer, CSWeaponType weaponType)
    {
        var localizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

        var currentPreference = arenaPlayer.WeaponPreferences.GetValueOrDefault(weaponType);

        var randomText = currentPreference == null
            ? localizer["k4.menu.guns.selected", localizer["k4.menu.guns.random"]]
            : localizer["k4.menu.guns.random"];

        var menuBuilder = _core.Menu
            .CreateMenu("k4.guns.selection")
            .WithTitle(localizer[Weapons.GetTranslationKey(weaponType)])
            .WithItemsPerPage(8);

        // Random
        menuBuilder.Add(new ButtonComponent(
            randomText,
            async ctx =>
            {
                var clickLocalizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

                arenaPlayer.SetWeaponPreference(weaponType, null);

                _ = _databaseService.SaveWeaponPreferenceAsync(
                    arenaPlayer,
                    weaponType,
                    null
                );

                await ctx.Player.SendChatAsync(
                    $"{clickLocalizer["k4.general.prefix"]} " +
                    $"{clickLocalizer["k4.chat.weapon_set_random", clickLocalizer[Weapons.GetTranslationKey(weaponType)]]}"
                );

                ShowWeaponSelectionMenu(arenaPlayer, weaponType);
            }
        ));

        // Weapons
        foreach (var weapon in Weapons.GetByType(weaponType))
        {
            var weaponName = _core.Helpers.GetClassnameByDefinitionIndex((int)weapon);
            var weaponKey = weaponName?.Replace("weapon_", "") ?? weapon.ToString();

            var translationKey = $"k4.weapon.{weaponKey}";
            var displayName = localizer[translationKey];

            var isSelected = currentPreference == weapon;

            var formattedName = isSelected
                ? localizer["k4.menu.guns.selected", displayName]
                : displayName;

            var capturedWeapon = weapon;
            var capturedDisplayName = displayName;

            menuBuilder.Add(new ButtonComponent(
                formattedName,
                async ctx =>
                {
                    var clickLocalizer = _core.Translation.GetPlayerLocalizer(ctx.Player);

                    arenaPlayer.SetWeaponPreference(weaponType, capturedWeapon);

                    _ = _databaseService.SaveWeaponPreferenceAsync(
                        arenaPlayer,
                        weaponType,
                        capturedWeapon
                    );

                    await ctx.Player.SendChatAsync(
                        $"{clickLocalizer["k4.general.prefix"]} " +
                        $"{clickLocalizer["k4.chat.weapon_set", capturedDisplayName]}"
                    );

                    ShowWeaponSelectionMenu(arenaPlayer, weaponType);
                }
            ));
        }

        var menu = menuBuilder.Build();
        menu.Open(arenaPlayer.Player);
    }
    public void ShowRoundPreferencesMenu(ArenaPlayer arenaPlayer)
    {
        var localizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

        var menuBuilder = _core.Menu
            .CreateMenu("k4.round.preferences")
            .WithTitle(localizer["k4.menu.rounds.title"])
            .WithItemsPerPage(8);

        foreach (var roundType in RoundTypes.All)
        {
            var isEnabled = arenaPlayer.HasRoundTypeEnabled(roundType);
            var capturedRoundType = roundType;

            menuBuilder.Add(new ToggleComponent(
                localizer[roundType.Name],
                defaultValue: isEnabled,
                onChanged: change =>
                {
                    var clickLocalizer = _core.Translation.GetPlayerLocalizer(change.Player);

                    var result = arenaPlayer.ToggleRoundType(capturedRoundType);

                    if (result == null)
                    {
                        _ = change.Player.SendChatAsync(
                            $"{clickLocalizer["k4.general.prefix"]} " +
                            $"{clickLocalizer["k4.chat.round_last_one"]}"
                        );

                        ShowRoundPreferencesMenu(arenaPlayer);
                        return;
                    }

                    _ = _databaseService.SaveRoundPreferenceAsync(
                        arenaPlayer,
                        capturedRoundType,
                        result.Value
                    );

                    var status = result.Value
                        ? clickLocalizer["k4.chat.round_enabled"]
                        : clickLocalizer["k4.chat.round_disabled"];

                    _ = change.Player.SendChatAsync(
                        $"{clickLocalizer["k4.general.prefix"]} " +
                        $"{clickLocalizer["k4.chat.round_toggled", clickLocalizer[capturedRoundType.Name], status]}"
                    );
                }
            ));
        }

        var menu = menuBuilder.Build();
        menu.Open(arenaPlayer.Player);
    }

}