using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace K4Arenas;

public partial class MenuServices
{

    public void CreateSettingsMenu(IPlayer _player, ArenaPlayer _arenaPlayer)
    {
        if (_player == null || !_player.IsValid) return;
        if (_arenaPlayer == null) return;
        var menu = Core.Menu.CreateMenu("poor.arenas")
        .WithTitle("Poor ARENAS");

        menu.Add(new SubmenuComponent(
            "Bronie",
            _ => ValueTask.FromResult<IMenu?>(BuildWeaponsMenu(_arenaPlayer)),
            "poor.arenas.weapons"
        ));

        menu.Add(new SubmenuComponent(
            "Tryby rund",
            _ => ValueTask.FromResult<IMenu?>(BuildRoundsMenu(_arenaPlayer)),
            "poor.arenas.rounds"
        ));

        menu.Build().Open(_player);
    }

    private IMenu BuildWeaponsMenu(ArenaPlayer arenaPlayer)
    {
        var localizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

        var menu = Core.Menu.CreateMenu("poor.arenas.weapons")
            .WithTitle("WYBIERZ KATEGORIĘ BRONI");

        var weaponCategories = new[]
        {
        (CSWeaponType.WEAPONTYPE_RIFLE, "Karabiny", "rifles"),
        (CSWeaponType.WEAPONTYPE_SNIPER_RIFLE, "Snajperki", "snipers"),
        (CSWeaponType.WEAPONTYPE_PISTOL, "Pistolety", "pistols"),
        (CSWeaponType.WEAPONTYPE_SHOTGUN, "Strzelby", "shotguns"),
        (CSWeaponType.WEAPONTYPE_SUBMACHINEGUN, "Pistolety maszynowe", "smgs"),
        (CSWeaponType.WEAPONTYPE_MACHINEGUN, "Karabiny maszynowe", "machineguns")
    };

        foreach (var (type, name, id) in weaponCategories)
        {
            var capturedType = type;

            menu.Add(new SubmenuComponent(
                name,
                _ => ValueTask.FromResult<IMenu?>(
                    BuildWeaponCategoryMenu(arenaPlayer, capturedType, name, id)
                ),
                $"poor.arenas.weapons.{id}"
            ));
        }

        return menu.Build();
    }


    private IMenu BuildWeaponCategoryMenu(
        ArenaPlayer arenaPlayer,
        CSWeaponType weaponType,
        string categoryName,
        string categoryId)
    {
        var localizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

        var menu = Core.Menu.CreateMenu($"poor.arenas.weapons.{categoryId}")
            .WithTitle(categoryName.ToUpperInvariant());

        var currentPreference =
            arenaPlayer.WeaponPreferences.GetValueOrDefault(weaponType);

        // Losowa broń
        var randomText = currentPreference == null
            ? localizer["k4.menu.guns.selected", localizer["k4.menu.guns.random"]]
            : localizer["k4.menu.guns.random"];

        menu.Add(new ButtonComponent(randomText, async ctx =>
        {
            arenaPlayer.SetWeaponPreference(weaponType, null);

            await _databaseService.SaveWeaponPreferenceAsync(
                arenaPlayer,
                weaponType,
                null
            );

            var clickLocalizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

            await arenaPlayer.Player.SendChatAsync(
                $"{clickLocalizer["k4.general.prefix"]} " +
                $"{clickLocalizer["k4.chat.weapon_set_random", clickLocalizer[Weapons.GetTranslationKey(weaponType)]]}"
            );
        }));

        // Konkretne bronie
        foreach (var weapon in Weapons.GetByType(weaponType))
        {
            var capturedWeapon = weapon;

            var weaponName = _core.Helpers.GetClassnameByDefinitionIndex((int)weapon);
            var weaponKey = weaponName?.Replace("weapon_", "") ?? weapon.ToString();

            var translationKey = $"k4.weapon.{weaponKey}";
            var displayName = localizer[translationKey];

            var isSelected = currentPreference == weapon;

            var buttonText = isSelected
                ? localizer["k4.menu.guns.selected", displayName]
                : displayName;

            menu.Add(new ButtonComponent(buttonText, async ctx =>
            {
                arenaPlayer.SetWeaponPreference(weaponType, capturedWeapon);

                await _databaseService.SaveWeaponPreferenceAsync(
                    arenaPlayer,
                    weaponType,
                    capturedWeapon
                );

                var clickLocalizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

                await arenaPlayer.Player.SendChatAsync(
                    $"{clickLocalizer["k4.general.prefix"]} " +
                    $"{clickLocalizer["k4.chat.weapon_set", displayName]}"
                );
            }));
        }

        return menu.Build();
    }
    private IMenu BuildRoundsMenu(ArenaPlayer arenaPlayer)
    {
        var localizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

        var menu = Core.Menu.CreateMenu("poor.arenas.rounds")
            .WithTitle("USTAWIENIA TRYBÓW RUND");

        foreach (var roundType in RoundTypes.All)
        {
            var capturedRoundType = roundType;

            var toggle = new ToggleComponent(
                localizer[roundType.Name],
                arenaPlayer.HasRoundTypeEnabled(roundType),
                change =>
                {
                    _ = HandleRoundToggleAsync(arenaPlayer, capturedRoundType);
                }
            )
            {
                OnColor = "#D1F811",
                OffColor = "#777777"
            };

            menu.Add(toggle);
        }

        return menu.Build();
    }
    private async Task HandleRoundToggleAsync(
        ArenaPlayer arenaPlayer,
        RoundType roundType)
    {
        var localizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

        var result = arenaPlayer.ToggleRoundType(roundType);

        // Nie pozwalamy wyłączyć ostatniego aktywnego trybu rundy.
        if (result == null)
        {
            await arenaPlayer.Player.SendChatAsync(
                $"{localizer["k4.general.prefix"]} " +
                $"{localizer["k4.chat.round_last_one"]}"
            );

            // Ponowne otwarcie menu przywraca prawidłowe wartości przełączników.
            BuildRoundsMenu(arenaPlayer).Open(arenaPlayer.Player);
            return;
        }

        await _databaseService.SaveRoundPreferenceAsync(
            arenaPlayer,
            roundType,
            result.Value
        );

        var status = result.Value
            ? localizer["k4.chat.round_enabled"]
            : localizer["k4.chat.round_disabled"];

        await arenaPlayer.Player.SendChatAsync(
            $"{localizer["k4.general.prefix"]} " +
            $"{localizer["k4.chat.round_toggled", localizer[roundType.Name], status]}"
        );
    }

}