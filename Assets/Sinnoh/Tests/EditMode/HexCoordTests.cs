using System;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    public class HexCoordTests
    {
        [Test]
        public void S3_1_이웃은서로다른6칸()
        {
            var c = new HexCoord(0, 0);
            var ns = c.Neighbors().ToList();
            Assert.AreEqual(6, ns.Count);
            Assert.AreEqual(6, ns.Distinct().Count());
            CollectionAssert.DoesNotContain(ns, c);
        }

        [Test]
        public void S3_1_이웃관계는대칭()
        {
            var c = new HexCoord(-13, -3);
            foreach (var n in c.Neighbors())
                CollectionAssert.Contains(n.Neighbors().ToList(), c, n.ToString());
        }

        [Test]
        public void M0_홀수좌표는거부()
        {
            Assert.Throws<ArgumentException>(() => new HexCoord(0, 1));
        }
    }
}
