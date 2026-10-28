// Core2CombatLogDrawer.cs — the combat log's drawer and its Full log door, as state: open or shut, its Small / Medium /
// Large size (tap toggles; a drag snaps to the nearest stop; ArrowUp / ArrowDown / Home / End step it), the door with
// its Full detail / Compact log toggle, and the readable player and enemy name colours. The rows themselves are the
// domain's (Core2CombatLog.Entries / View): a legend, a "Round N" heading per round, one line per entry and, in full
// detail, each hit's damage receipt.
//
// JS at the pin:
//   src/ui/components/combatTools.js     the Log button (tap, drag with combatLogDragScale and combatLogSnapSize, the
//                                        arrow keys), the drawer panel, the Full log door
//   src/ui/components/combatLogModal.js  the door: legend, entries, Close and the detail toggle
//   src/ui/models/CombatToolsModel.js    COMBAT_LOG_SIZES, COMBAT_LOG_DEFAULT_SIZE, combatLogHeight
//   src/ui/models/CombatLogPaletteModel.js combatLogPalette (the Settings colours fall back to readable defaults)
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Core2;

namespace AshenSpire.Presentation.Core2
{
    public sealed class Core2CombatLogDrawer
    {
        private readonly Core2Content _content;
        public IReadOnlyList<string> Sizes { get; }
        public bool Open { get; private set; }
        public string Size { get; private set; }
        public bool FullOpen { get; private set; }
        public bool Detailed { get; private set; }
        /// <summary>The Settings name colours (combatLogPlayerColor / combatLogEnemyColor); null reads the defaults.</summary>
        public string PlayerColorSetting { get; set; }
        public string EnemyColorSetting { get; set; }

        public Core2CombatLogDrawer(Core2Content content)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            Sizes = Core2CombatLog.Sizes(content);
            Size = Core2CombatLog.DefaultSize(content);
        }

        public string Title => Core2CombatText.T("combat.log.title");
        public string FullLabel => Core2CombatText.T("combat.log.full");
        public string DetailToggleLabel => Core2CombatLog.DetailToggleLabel(_content, Detailed);
        public string CloseLabel => Core2CombatText.T("common.close");
        /// <summary>JS aria-description of the Log button.</summary>
        public string Description => Size + ". Tap to open or close; drag up or down to resize. Arrow keys change size.";

        public (string Player, string Enemy) Palette => Core2CombatLog.Palette(_content,
            PlayerColorSetting ?? (string)_content.Table("ui/combat.json")["combatLog"]["palette"]["player"],
            EnemyColorSetting ?? (string)_content.Table("ui/combat.json")["combatLog"]["palette"]["enemy"]);

        public void Toggle() => Open = !Open;
        public void Close() => Open = false;
        public void OpenFull() => FullOpen = true;
        public void CloseFull() => FullOpen = false;
        public void ToggleDetail() => Detailed = !Detailed;

        /// <summary>JS keydown on the Log button: ArrowUp grows, ArrowDown shrinks, Home is Small, End is Large. True when handled.</summary>
        public bool Key(string key)
        {
            var index = Sizes.ToList().IndexOf(Size);
            switch (key)
            {
                case "Home": Size = Sizes[0]; break;
                case "End": Size = Sizes[Sizes.Count - 1]; break;
                case "ArrowUp": Size = Sizes[Math.Max(0, Math.Min(Sizes.Count - 1, index + 1))]; break;
                case "ArrowDown": Size = Sizes[Math.Max(0, Math.Min(Sizes.Count - 1, index - 1))]; break;
                default: return false;
            }
            Open = true;
            return true;
        }

        /// <summary>JS combatLogHeight for every stop, for the space between the HUD's bottom and the dock's top.</summary>
        public Dictionary<string, double> Heights(double cardHeight, double viewportHeight, double dockTop, double menuBottom) =>
            Sizes.ToDictionary(size => size, size => Core2CombatLog.Height(size, cardHeight, viewportHeight, dockTop, menuBottom), StringComparer.Ordinal);

        /// <summary>The height a drag shows: the stop it started from plus the travel times the drag scale, clamped to Small–Large.</summary>
        public double DragHeight(IReadOnlyDictionary<string, double> heights, double startHeight, double deltaUp, double viewportHeight, double handleBottom)
        {
            var scale = Core2CombatLog.DragScale(heights, viewportHeight, handleBottom);
            return Math.Max(heights["Small"], Math.Min(heights["Large"], startHeight + deltaUp * scale));
        }

        /// <summary>JS release of a drag: the nearest stop, and the drawer stays open.</summary>
        public void EndDrag(IReadOnlyDictionary<string, double> heights, double height)
        {
            Size = Core2CombatLog.SnapSize(height, heights, Sizes);
            Open = true;
        }

        /// <summary>The drawer's rows (legend, Round headings, entries; the door adds receipts in full detail).</summary>
        public List<Core2LogViewRow> Rows(Core2Fight fight, bool door)
        {
            var entries = Core2CombatLog.Entries(_content, fight.Events, fight.Enemies);
            return Core2CombatLog.View(_content, entries, door && Detailed, legend: true);
        }
    }
}
