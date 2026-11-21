using System;
using System.Collections.Generic;
using System.Text;

namespace Sinnoh.Rules.Tutorial
{
    /// <summary>
    /// 각본의 답 표기(07 §7 — `allow`·`auto`·봇 큐): 적은 칸만 맞춰 보는 부분 일치. 비운 칸(null)은 아무 값이나.
    /// 답(Answer) 꼴은 선택 종류·답 종류·Target·Arg·Values를, 정비(Anytime) 꼴은 정비 종류·파티 칸·Slot·Value·Values를 본다. 늘 엔진의 합법 답 안에서 고른다 — 표기는 그 부분집합을 가리킬 뿐이다.
    /// 정비 꼴(07 T26): 포켓몬은 상태 Id가 아니라 내 파티 칸 <see cref="Party"/>로 적고(판마다 Id가 다르다), PartyOrder의 Values도 파티 칸 순서로 적는다 — 진행기가 그때의 파티로 바꾼다.
    /// </summary>
    public sealed class AnswerPattern
    {
        public ChoiceKind? Choice;
        public AnswerKind? Kind;
        public int? Target;
        public int? Arg;
        /// <summary>null = 아무 목록. 있으면 순서까지 같아야 한다. 정비 PartyOrder에서는 파티 칸 순서.</summary>
        public int[] Values;
        /// <summary>true = 정비 명령(Anytime) 꼴. Maint가 null이면 모든 정비.</summary>
        public bool IsMaint;
        public MaintKind? Maint;
        /// <summary>정비 대상 포켓몬 = 내 파티 칸(0~5).</summary>
        public int? Party;
        /// <summary>정비 Maintenance.Slot.</summary>
        public int? Slot;
        /// <summary>정비 Maintenance.Value(기술·아이템·포켓몬 표 색인 · −1 = 비움).</summary>
        public int? Value;

        /// <summary>아무 답(그 선택의 첫 합법 답).</summary>
        public static AnswerPattern Any() => new AnswerPattern();

        public static AnswerPattern Of(AnswerKind kind, int? target = null, int? arg = null) => new AnswerPattern { Kind = kind, Target = target, Arg = arg };

        /// <summary>그 선택 종류의 Pick 답(target = null이면 아무거나).</summary>
        public static AnswerPattern Pick(ChoiceKind choice, int? target = null) => new AnswerPattern { Choice = choice, Kind = AnswerKind.Pick, Target = target };

        /// <summary>그 선택 종류의 아무 답.</summary>
        public static AnswerPattern On(ChoiceKind choice) => new AnswerPattern { Choice = choice };

        public static AnswerPattern Maintain(MaintKind? kind = null) => new AnswerPattern { IsMaint = true, Maint = kind };

        /// <summary>선택 c의 답 a에 맞나(정비 꼴은 늘 아니다).</summary>
        public bool Matches(PendingChoice c, ChoiceAnswer a)
        {
            if (IsMaint || a == null) return false;
            if (Choice.HasValue && (c == null || c.Kind != Choice.Value)) return false;
            if (Kind.HasValue && a.Kind != Kind.Value) return false;
            if (Target.HasValue && a.Target != Target.Value) return false;
            if (Arg.HasValue && a.Arg != Arg.Value) return false;
            if (Values != null)
            {
                if (a.Values.Count != Values.Length) return false;
                for (int i = 0; i < Values.Length; i++)
                    if (a.Values[i] != Values[i]) return false;
            }
            return true;
        }

        /// <summary>정비 m에 맞나. party = 내 파티(포켓몬 Id, 빈 칸 −1) — Party·PartyOrder Values를 견줄 때 쓴다(null이면 그 칸을 적은 표기는 맞지 않는다).</summary>
        public bool Matches(Maintenance m, IReadOnlyList<int> party = null)
        {
            if (!IsMaint || m == null) return false;
            if (Maint.HasValue && m.Kind != Maint.Value) return false;
            if (Party.HasValue && (party == null || Party.Value < 0 || Party.Value >= party.Count || party[Party.Value] != m.Pokemon)) return false;
            if (Slot.HasValue && m.Slot != Slot.Value) return false;
            if (Value.HasValue && m.Value != Value.Value) return false;
            if (Values != null)
            {
                List<int> want;
                try { want = MaintValues(m.Kind, party); }
                catch (ArgumentException) { return false; }
                if (want == null || m.Values.Count != want.Count) return false;
                for (int i = 0; i < want.Count; i++)
                    if (m.Values[i] != want[i]) return false;
            }
            return true;
        }

        /// <summary>자동 진행이 낼 정비 명령(07 T26): Maint는 꼭 있어야 하고, 적지 않은 칸은 −1·빈 목록. party = 그때의 내 파티.</summary>
        public Maintenance ToMaintenance(IReadOnlyList<int> party)
        {
            if (!IsMaint || !Maint.HasValue) throw new InvalidOperationException($"{this}: 정비 종류가 없는 표기로는 정비를 낼 수 없다");
            var m = new Maintenance { Kind = Maint.Value, Slot = Slot ?? -1, Value = Value ?? -1 };
            if (Party.HasValue) m.Pokemon = PartyAt(party, Party.Value);
            if (Values != null) m.Values.AddRange(MaintValues(m.Kind, party));
            return m;
        }

        List<int> MaintValues(MaintKind kind, IReadOnlyList<int> party)
        {
            if (kind != MaintKind.PartyOrder) return new List<int>(Values);
            var ids = new List<int>(Values.Length);
            foreach (int slot in Values) ids.Add(PartyAt(party, slot));
            return ids;
        }

        static int PartyAt(IReadOnlyList<int> party, int slot)
        {
            if (party == null || slot < 0 || slot >= party.Count || party[slot] < 0) throw new ArgumentException($"파티 칸 {slot}이 비었다");
            return party[slot];
        }

        /// <summary>합법 답 가운데 처음 맞는 것(없으면 null).</summary>
        public ChoiceAnswer First(PendingChoice c, List<ChoiceAnswer> legal)
        {
            foreach (var a in legal)
                if (Matches(c, a)) return a;
            return null;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            if (IsMaint)
            {
                sb.Append("Maint(").Append(Maint.HasValue ? Maint.Value.ToString() : "*").Append(')');
                if (Party.HasValue) sb.Append(" p=").Append(Party.Value);
                if (Slot.HasValue) sb.Append(" s=").Append(Slot.Value);
                if (Value.HasValue) sb.Append(" v=").Append(Value.Value);
            }
            else
            {
                sb.Append(Choice.HasValue ? Choice.Value.ToString() : "*").Append(':').Append(Kind.HasValue ? Kind.Value.ToString() : "*");
                if (Target.HasValue) sb.Append(" t=").Append(Target.Value);
                if (Arg.HasValue) sb.Append(" a=").Append(Arg.Value);
            }
            if (Values != null) sb.Append(" [").Append(string.Join(",", Values)).Append(']');
            return sb.ToString();
        }
    }
}
