using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>이벤트를 좌석에 거른(Engine.FilterFor) 뒤 모든 필드를 글로 편다. 보기 누출 시험이 두 열을 글로 비교한다.</summary>
    static class EventDump
    {
        public static string Of(List<GameEvent> events, int viewer)
        {
            var sb = new StringBuilder();
            foreach (var e in events)
            {
                var f = Engine.FilterFor(e, viewer);
                if (f == null) sb.Append("(안 보냄)");
                else Value(f, sb);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public static string One(object o)
        {
            var sb = new StringBuilder();
            Value(o, sb);
            return sb.ToString();
        }

        static void Value(object v, StringBuilder sb)
        {
            switch (v)
            {
                case null:
                    sb.Append("null");
                    return;
                case string str:
                    sb.Append('"').Append(str).Append('"');
                    return;
                case ChoiceAnswer a:
                    sb.Append(a);
                    return;
                case SourceRef r:
                    sb.Append(r.Kind).Append(':').Append(r.Id);
                    return;
            }
            var t = v.GetType();
            if (t.IsPrimitive || t.IsEnum)
            {
                sb.Append(v);
                return;
            }
            if (v is IEnumerable list)
            {
                sb.Append('[');
                foreach (var x in list)
                {
                    Value(x, sb);
                    sb.Append(',');
                }
                sb.Append(']');
                return;
            }
            sb.Append(t.Name).Append('{');
            var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance);
            Array.Sort(fields, (x, y) => x.MetadataToken.CompareTo(y.MetadataToken));
            foreach (var f in fields)
            {
                sb.Append(f.Name).Append('=');
                Value(f.GetValue(v), sb);
                sb.Append(' ');
            }
            sb.Append('}');
        }
    }
}
