using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 표 하나의 문자열 키 ↔ 조밀한 int 색인(01 §10·§11). 색인 = 키 목록의 순서(생성 JSON의 행 순서)다.
    /// 엔진은 int만 쓰고, 세이브는 표마다 이 키 목록을 남겨 데이터가 바뀌어도 색인을 다시 맞춘다(SaveRecord.IdTable).
    /// 엄격하다: 빈 키·중복 키는 만들 때, 모르는 키는 찾을 때 예외다.
    /// </summary>
    public sealed class IdTable
    {
        readonly string[] keys;
        readonly Dictionary<string, int> index;

        /// <summary>표 이름(예: "pokemon"). 오류 문구에만 쓴다.</summary>
        public readonly string Name;

        public IdTable(string name, IReadOnlyList<string> keys)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            this.keys = new string[keys.Count];
            index = new Dictionary<string, int>(keys.Count, StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                var k = keys[i];
                if (string.IsNullOrEmpty(k)) throw new ArgumentException($"{name} 색인 {i}: 빈 키", nameof(keys));
                if (index.ContainsKey(k)) throw new ArgumentException($"{name} 키 \"{k}\"가 두 번 나온다", nameof(keys));
                index.Add(k, i);
                this.keys[i] = k;
            }
        }

        public int Count => keys.Length;

        /// <summary>색인 → 키.</summary>
        public string this[int id] => keys[id];

        /// <summary>키 → 색인. 모르는 키는 KeyNotFoundException.</summary>
        public int IndexOf(string key)
        {
            if (key != null && index.TryGetValue(key, out int id)) return id;
            throw new KeyNotFoundException($"{Name}에 키 \"{key}\"가 없다");
        }

        public bool TryIndexOf(string key, out int id)
        {
            if (key != null) return index.TryGetValue(key, out id);
            id = -1;
            return false;
        }
    }
}
