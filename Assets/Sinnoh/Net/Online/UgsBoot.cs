using System;
using System.Text;
using System.Threading.Tasks;
using Sinnoh.Data;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace Sinnoh.Net
{
    /// <summary>[인터넷] 방의 UGS 실패(코드 = <see cref="OnlineFail"/>).</summary>
    public sealed class OnlineError : Exception
    {
        public OnlineError(string code, Exception inner) : base(code + ": " + inner?.Message, inner) => Code = code;
        public string Code { get; }
    }

    /// <summary>
    /// UGS 초기화 + 익명 로그인만(isekai Net/Online/UgsBoot을 고쳐 옮김 — 계정·환경·클라우드 끔은 범위 밖, 02 §9). 프로젝트는 ProjectSettings `cloudProjectId`(결정 92), 환경은 기본 production.
    /// UGS 프로필(세션 토큰이 PlayerPrefs에 프로필마다 하나) = <see cref="ProfileOverride"/> 또는 실행 인자 -profile 값 — 한 PC의 두 창이 다른 익명 플레이어가 되게.
    /// 실패는 <see cref="OnlineError"/>(<see cref="OnlineFail.RelayAuth"/> — 화면은 [직접 연결] 안내, 결정 92). Unity Services는 Play 모드·플레이어에서만 초기화된다.
    /// </summary>
    public static class UgsBoot
    {
        const int MaxProfile = 30;   // UGS 프로필 규칙 ^[a-zA-Z0-9_-]{1,30}$

        static Task initTask;
        static Task signInTask;

        /// <summary>시험용: 있으면 -profile 값 대신 쓴다.</summary>
        public static string ProfileOverride { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            initTask = signInTask = null;
            ProfileOverride = null;
        }

        /// <summary>초기화(한 번 — 같은 Task를 나누고, 실패하면 다음 호출이 다시) + 익명 로그인(이미 로그인했으면 그대로). 실패 = OnlineError(RelayAuth).</summary>
        public static async Task SignInAsync()
        {
            try
            {
                await EnsureInitializedAsync();
                if (AuthenticationService.Instance.IsSignedIn) return;
                if (signInTask == null || signInTask.IsFaulted || signInTask.IsCanceled) signInTask = AuthenticationService.Instance.SignInAnonymouslyAsync();
                await signInTask;
            }
            catch (OnlineError) { throw; }
            catch (Exception e) { throw new OnlineError(OnlineFail.RelayAuth, e); }
        }

        /// <summary>로그인한 익명 플레이어 ID(로그). 없으면 null.</summary>
        public static string PlayerId => UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn ? AuthenticationService.Instance.PlayerId : null;

        static Task EnsureInitializedAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Initialized) return Task.CompletedTask;
            if (initTask == null || initTask.IsFaulted || initTask.IsCanceled) initTask = InitializeAsync();
            return initTask;
        }

        static async Task InitializeAsync()
        {
            var options = new InitializationOptions();
            var profile = ProfileFor(Environment.GetCommandLineArgs());
            if (profile != null) options.SetProfile(profile);
            Debug.Log($"[Net] UGS 초기화: 프로필 {profile ?? "(기본)"}");
            await UnityServices.InitializeAsync(options);
        }

        /// <summary>UGS 프로필 이름: ProfileOverride 또는 마지막 「-profile 값」, 없으면 null(기본). 규칙 밖 글자는 '_'로, 바꿨거나 30자를 넘으면 앞 23자 + '_' + 해시 6자.</summary>
        public static string ProfileFor(string[] args)
        {
            string name = string.IsNullOrEmpty(ProfileOverride) ? ProfileArg(args) : ProfileOverride;
            return name == null ? null : SafeProfile(name);
        }

        static string ProfileArg(string[] args)
        {
            if (args == null) return null;
            for (int i = args.Length - 2; i >= 0; i--)
                if (args[i] == "-profile" && !string.IsNullOrWhiteSpace(args[i + 1])) return args[i + 1].Trim();
            return null;
        }

        static string SafeProfile(string name)
        {
            var sb = new StringBuilder(name.Length);
            bool changed = false;
            foreach (char c in name)
            {
                bool ok = c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-';
                sb.Append(ok ? c : '_');
                if (!ok) changed = true;
            }
            if (!changed && sb.Length <= MaxProfile) return sb.ToString();
            uint h = 2166136261;   // FNV-1a — 실행마다 같은 값
            foreach (char c in name)
            {
                h ^= c;
                h *= 16777619;
            }
            return sb.ToString(0, Math.Min(sb.Length, MaxProfile - 7)) + "_" + h.ToString("x8").Substring(0, 6);
        }
    }
}
