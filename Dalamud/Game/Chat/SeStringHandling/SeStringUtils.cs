using Dalamud.Game.Chat.SeStringHandling.Payloads;
using Lumina.Excel.GeneratedSheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DalamudItem = Dalamud.Data.TransientSheet.Item;

namespace Dalamud.Game.Chat.SeStringHandling
{
    public class SeStringUtils
    {
        public static SeString CreateItemLink(uint itemId, bool isHQ, string displayNameOverride = null)
        {
            string displayName = displayNameOverride ?? SeString.Dalamud.Data.GetExcelSheet<DalamudItem>().GetRow((int)itemId).Name;
            if (isHQ)
            {
                displayName += " \uE03C";
            }

            var payloads = new List<Payload>(new Payload[]
            {
                new UIForegroundPayload(0x0225),
                new UIGlowPayload(0x0226),
                new ItemPayload(itemId, isHQ),
                new UIForegroundPayload(0x01F4),
                new UIGlowPayload(0x01F5),
                new TextPayload("\uE0BB"),
                new UIGlowPayload(0),
                new UIForegroundPayload(0),
                new TextPayload(displayName),
                new RawPayload(new byte[] { 0x02, 0x27, 0x07, 0xCF, 0x01, 0x01, 0x01, 0xFF, 0x01, 0x03 })
            });

            return new SeString(payloads);
        }

        public static SeString CreateMapLink(uint territoryId, uint mapId, float xCoord, float yCoord, float fudgeFactor = 0.05f)
        {
            var mapPayload = new MapLinkPayload(territoryId, mapId, xCoord, yCoord, fudgeFactor);
            var nameString = $"{mapPayload.PlaceName} {mapPayload.CoordinateString}";

            var payloads = new List<Payload>(new Payload[]
            {
                mapPayload,
                new UIForegroundPayload(0x01F4),
                new UIGlowPayload(0x01F5),
                new TextPayload("\uE0BB"),
                new UIGlowPayload(0),
                new UIForegroundPayload(0),
                new TextPayload(nameString),
                new RawPayload(new byte[] { 0x02, 0x27, 0x07, 0xCF, 0x01, 0x01, 0x01, 0xFF, 0x01, 0x03 })
            });

            return new SeString(payloads);
        }

        public static SeString CreateMapLink(string placeName, float xCoord, float yCoord, float fudgeFactor = 0.05f)
        {
            var mapSheet = SeString.Dalamud.Data.GetExcelSheet<Map>();

            var matches = SeString.Dalamud.Data.GetExcelSheet<PlaceName>().GetRows()
                .Where(row => row.Name.ToLowerInvariant() == placeName.ToLowerInvariant())
                .ToArray();

            foreach (var place in matches)
            {
                var map = mapSheet.GetRows().FirstOrDefault(row => row.PlaceName == place.RowId);
                if (map != null)
                {
                    return CreateMapLink(map.TerritoryType, (uint)map.RowId, xCoord, yCoord);
                }
            }

            // TODO: empty? throw?
            return null;
        }
    }
}
