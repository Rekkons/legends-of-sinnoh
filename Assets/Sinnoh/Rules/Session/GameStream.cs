using System;
using System.Collections.Generic;

namespace Sinnoh.Rules.Session
{
    /// <summary>
    /// 좌석 보기 전체(02 §4-3 GameSnapshot): 게임 시작 · 재접속 · RequestResync · 손님 쪽 기준이 어긋날 때. Sections = 그 좌석 ViewFor의 <see cref="StateStream"/>,
    /// Hash = 그 스트림의 해시(= StateHash.Of(보기)) — 손님이 되읽은 뒤 맞대 본다. 이벤트는 그 좌석에 거른 것(재접속·Resync는 비었다).
    /// </summary>
    public sealed class GameSnapshot : ServerMsg
    {
        public int Version;
        public int Viewer;
        public int[][] Sections;
        public ulong Hash;
        public List<GameEvent> Events;
        public long AnimEndMs;
        public ClockState Clock;
        /// <summary>이 좌석의 「지금 무를 수 있나」 · 되돌림 알림(<see cref="GameUpdate.Undo"/> · <see cref="GameUpdate.Rewind"/> 그대로 — 결정 149). 평소 Rewind는 null, 끈 판은 둘 다 null.</summary>
        public UndoOffer Undo;
        public RewindNote Rewind;
    }

    /// <summary>
    /// 갱신 하나의 델타(02 §4-3 GameDelta): BaseVersion = 손님이 가진 보기의 판. Patch = <see cref="StreamPatch"/>(BaseVersion 스트림 → Version 스트림),
    /// Hash = 적용 뒤 스트림의 해시. 손님은 BaseVersion이 자기 판과 같을 때만 적용하고, 해시가 다르면 RequestResync(02 §4-4).
    /// </summary>
    public sealed class GameDelta : ServerMsg
    {
        public int BaseVersion;
        public int Version;
        public int[] Patch;
        public ulong Hash;
        public List<GameEvent> Events;
        public long AnimEndMs;
        public ClockState Clock;
        /// <summary>이 좌석의 「지금 무를 수 있나」 · 되돌림 알림(<see cref="GameUpdate.Undo"/> · <see cref="GameUpdate.Rewind"/> 그대로 — 결정 149). 평소 Rewind는 null, 끈 판은 둘 다 null.</summary>
        public UndoOffer Undo;
        public RewindNote Rewind;
    }

    /// <summary>스트림 메시지 크기 경고(02 §4-7) — 실제 바이트를 아는 MessageCodec(Net)이 넘으면 경고를 남긴다 [가정 M7-16]. M7_크기 시험이 이 안인지 본다.</summary>
    public static class StreamBudget
    {
        public const int SnapshotWarnBytes = 64 * 1024;
        public const int DeltaWarnBytes = 16 * 1024;
        /// <summary>Relay를 지나는 좌석당 평균 목표 바이트/초(02 §4-7 [가정] 0.3 KiB/s, 조건 4 — Rules는 부동소수를 쓰지 않는다).</summary>
        public const int TargetBytesPerSecond = 307;
    }

    /// <summary>
    /// 호스트 쪽 좌석 하나의 스트림(02 §4-4): 그 좌석에 마지막으로 보낸 스트림과 판을 기억하고, 갱신(GameUpdate — 보기 사본 전체)을 스냅샷이나 델타로 바꾼다.
    /// 기억이 없으면(처음 · <see cref="Reset"/> 뒤 — 끊김 · 재접속 · Resync) 스냅샷이다. 끊긴 동안의 갱신은 쌓지 않는다 — 방이 Reset하고 돌아오면 스냅샷 하나(02 §4-4 끝).
    /// </summary>
    public sealed class SeatStream
    {
        int[][] last;
        int version;

        /// <summary>다음 갱신이 델타인가(기억이 있다).</summary>
        public bool Synced => last != null;

        public void Reset() => last = null;

        public ServerMsg Encode(GameUpdate u)
        {
            if (u?.View == null) throw new ArgumentException("보기가 없는 갱신", nameof(u));
            var now = StateStream.Of(u.View);
            ulong hash = StateStream.Hash(now);
            ServerMsg msg;
            if (last == null || last.Length != now.Length)
                msg = new GameSnapshot { Version = u.Version, Viewer = u.View.Viewer, Sections = now, Hash = hash, Events = u.Events, AnimEndMs = u.AnimEndMs, Clock = u.Clock, Undo = u.Undo, Rewind = u.Rewind };
            else
                msg = new GameDelta { BaseVersion = version, Version = u.Version, Patch = StreamPatch.Diff(last, now), Hash = hash, Events = u.Events, AnimEndMs = u.AnimEndMs, Clock = u.Clock, Undo = u.Undo, Rewind = u.Rewind };
            last = now;
            version = u.Version;
            return msg;
        }
    }

    /// <summary>
    /// 손님 쪽 스트림 적용(02 §4-4): 스냅샷·델타를 받아 보기(GameState)로 되읽고 지금의 <see cref="GameUpdate"/> 꼴(보기 사본 전체 + 이벤트 + 시계)로 내놓는다 —
    /// 화면·<see cref="SeatClient"/>는 전과 같은 GameUpdate를 받는다 [가정 M7-15]. 델타의 BaseVersion이 내 판과 다르거나, 패치 모양이 틀리거나, 적용한 스트림의 해시가 다르면
    /// 적용하지 않고 <see cref="NeedsResync"/> — 부르는 쪽(Net 손님)이 RequestResync를 보내고, 다음 스냅샷까지 델타를 버린다.
    /// </summary>
    public sealed class StreamReceiver
    {
        int[][] sections;

        /// <summary>지금 보기의 판(받은 것이 없으면 −1).</summary>
        public int Version { get; private set; } = -1;
        /// <summary>마지막으로 되읽은 보기(없으면 null). 받을 때마다 새 사본이다.</summary>
        public GameState View { get; private set; }
        /// <summary>기준이 어긋나 스냅샷을 기다린다.</summary>
        public bool NeedsResync { get; private set; }
        public int Snapshots { get; private set; }
        public int Deltas { get; private set; }
        /// <summary>버린 메시지 수(어긋난 델타 · 해시가 다른 스냅샷·델타).</summary>
        public int Dropped { get; private set; }
        /// <summary>마지막으로 버린 까닭(없으면 null).</summary>
        public string LastProblem { get; private set; }

        /// <summary>스냅샷·델타면 적용해 갱신을 돌려준다. 어긋났거나 다른 메시지면 null(어긋남은 NeedsResync).</summary>
        public GameUpdate Receive(ServerMsg msg)
        {
            switch (msg)
            {
                case GameSnapshot s:
                    return Take(s.Sections, s.Hash, s.Version, s.Viewer, s.Events, s.AnimEndMs, s.Clock, s.Undo, s.Rewind, true);
                case GameDelta d:
                    if (sections == null || NeedsResync) return Drop("스냅샷 전 델타");
                    if (d.BaseVersion != Version) return Drop($"델타 기준 {d.BaseVersion} ≠ 내 판 {Version}");
                    int[][] next;
                    try
                    {
                        next = StreamPatch.Apply(sections, d.Patch ?? new int[0]);
                    }
                    catch (FormatException e)
                    {
                        return Drop("패치 모양: " + e.Message);
                    }
                    return Take(next, d.Hash, d.Version, View.Viewer, d.Events, d.AnimEndMs, d.Clock, d.Undo, d.Rewind, false);
            }
            return null;
        }

        GameUpdate Take(int[][] next, ulong hash, int version, int viewer, List<GameEvent> events, long animEnd, ClockState clock, UndoOffer undo, RewindNote rewind, bool snapshot)
        {
            if (next == null) return Drop("구역 없음");
            if (StateStream.Hash(next) != hash) return Drop($"해시가 다르다({(snapshot ? "스냅샷" : "델타")} {version})");
            GameState view;
            try
            {
                view = StateStream.Read(next, viewer);
            }
            catch (FormatException e)
            {
                return Drop("되읽기: " + e.Message);
            }
            sections = next;
            Version = version;
            View = view;
            NeedsResync = false;
            if (snapshot) Snapshots++;
            else Deltas++;
            return new GameUpdate { Version = version, View = view, Events = events ?? new List<GameEvent>(), AnimEndMs = animEnd, Clock = clock, Undo = undo, Rewind = rewind };
        }

        GameUpdate Drop(string why)
        {
            NeedsResync = true;
            Dropped++;
            LastProblem = why;
            return null;
        }
    }
}
