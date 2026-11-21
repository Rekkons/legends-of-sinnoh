using System;
using System.Collections.Generic;
using Sinnoh.Rules;
using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 글자가 박힌 아이콘(타입 · 상태 이상)의 그림 키를 언어로 고르는 한 곳(결정 115).
    /// <list type="bullet">
    /// <item>한국어(<c>ko</c> · 값 없음) = 한국어판 Pt 그림 <c>Stage/Icons/type_&lt;타입&gt;.png</c> · <c>status_&lt;상태&gt;.png</c>.</item>
    /// <item>영어(<c>en</c>) = 영문판 Pt 그림 <c>Stage/Icons/en/…</c>(Tools/assets/make-icons.mjs의 LANGS — 같은 이름 · 같은 크기 32×16).</item>
    /// </list>
    /// 글자가 없는 그림(기술 분류 · 체력 상자 · HP 숫자)은 한 벌뿐이라 여기를 거치지 않는다.
    /// 타입 칩 · 상태 칩을 그리는 화면은 키를 직접 잇지 않고 <see cref="Type(PokemonType)"/> · <see cref="Status(StatusEffect)"/>를 부른다.
    /// </summary>
    public static class IconArt
    {
        public const string Folder = "Stage/Icons/";
        public const string Korean = "ko", English = "en";

        /// <summary>지금 화면 언어(<c>ko</c> · <c>en</c> — null은 한국어). 화면 언어를 정하는 곳이 넣는다(무대가 문자열 표를 받을 때 · 바꿀 때).</summary>
        public static string Lang;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Lang = null;

        /// <summary>지금 언어의 타입 아이콘 키.</summary>
        public static string Type(PokemonType type) => Type(type, Lang);

        /// <summary>그 언어의 타입 아이콘 키.</summary>
        public static string Type(PokemonType type, string lang) => Folder + Sub(lang) + "type_" + type + ".png";

        /// <summary>지금 언어의 상태 이상 아이콘 키.</summary>
        public static string Status(StatusEffect status) => Status(status, Lang);

        /// <summary>그 언어의 상태 이상 아이콘 키.</summary>
        public static string Status(StatusEffect status, string lang) => Folder + Sub(lang) + "status_" + status + ".png";

        /// <summary>그 언어의 타입 · 상태 아이콘 키 전부(타입 19 + 상태 6 — 번들 · 그림 시험이 센다).</summary>
        public static List<string> All(string lang)
        {
            var keys = new List<string>();
            foreach (PokemonType type in Enum.GetValues(typeof(PokemonType))) keys.Add(Type(type, lang));
            foreach (StatusEffect status in Enum.GetValues(typeof(StatusEffect)))
                if (status != StatusEffect.None) keys.Add(Status(status, lang));
            return keys;
        }

        static string Sub(string lang) => lang == English ? "en/" : "";
    }
}
