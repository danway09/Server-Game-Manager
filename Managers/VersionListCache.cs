using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MCServerManager.Managers
{
    // Кэш списков версий ядер (в памяти, на время работы приложения), чтобы
    // переключение ядра в интерфейсе не дёргало сеть заново каждый раз.
    // Кнопка «обновить» запрашивает список заново (forceRefresh: true).
    public static class VersionListCache
    {
        private static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(10);

        private static readonly ConcurrentDictionary<string, Entry> Cache = new();

        private sealed class Entry
        {
            public List<string> Items { get; set; } = new();

            public DateTime StampUtc { get; set; }
        }

        public static async Task<List<string>> GetAsync(
            string key,
            Func<Task<List<string>>> factory,
            bool forceRefresh = false)
        {
            if (!forceRefresh &&
                Cache.TryGetValue(key, out Entry? entry) &&
                DateTime.UtcNow - entry.StampUtc < EntryLifetime)
            {
                return entry.Items;
            }

            List<string> items = await factory();

            Cache[key] = new Entry
            {
                Items = items,
                StampUtc = DateTime.UtcNow
            };

            return items;
        }
    }
}
