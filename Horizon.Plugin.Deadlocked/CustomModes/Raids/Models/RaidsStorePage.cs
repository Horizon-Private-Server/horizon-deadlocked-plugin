using System;
using System.IO;

namespace Horizon.Plugin.Deadlocked.CustomModes.Raids.Models
{
    public class RaidsStorePage
    {
        // Keep the hub's store page layout in sync; the entire reply fits one message.
        public const int MAX_ITEMS = 12;
        public const int TITLE_LENGTH = 32;
        public const int SERIALIZED_SIZE = 20 + TITLE_LENGTH + MAX_ITEMS * 32;

        public uint RequestId { get; set; }
        public int Page { get; set; }
        public int TotalPages { get; set; }
        public int RotateInSeconds { get; set; }
        public string Title { get; set; } = string.Empty;
        public RaidsInventoryItem[] Items { get; set; } = [];

        public void Serialize(BinaryWriter writer)
        {
            if (Items.Length > MAX_ITEMS) throw new InvalidOperationException("Too many items in a store page.");
            writer.Write(RequestId);
            writer.Write(Page);
            writer.Write(TotalPages);
            writer.Write(Items.Length);
            writer.Write(RotateInSeconds);
            for (int i = 0; i < MAX_ITEMS; ++i)
                (i < Items.Length ? Items[i] : RaidsInventoryItem.Empty).Serialize(writer);
            writer.Write(Title, TITLE_LENGTH);
        }
    }
}
