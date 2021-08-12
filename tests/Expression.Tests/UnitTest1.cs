using System;
using System.Collections.Generic;
using Xunit;
using Expression;

namespace Expression.Tests
{
	public class UnitTest1
	{
		[Fact]
		public void Test01()
		{
			var vars = new Dictionary<string, decimal>();
			var str = "-12.5";
			var expr = new Expression(str);
			var res = expr.Evaluate(vars);
			Assert.Equal(-12.5m, res);
		}

		[Fact]
		public void Test02()
		{
			var vars = new Dictionary<string, decimal>();
			var str = "-12.5-4";
			var expr = new Expression(str);
			var res = expr.Evaluate(vars);
			Assert.Equal(-16.5m, res);
		}
		[Fact]
		public void Test03()
		{
			var vars = new Dictionary<string, decimal>();
			var str = "-(12.5+4)";
			var expr = new Expression(str);
			var res = expr.Evaluate(vars);
			Assert.Equal(-16.5m, res);
		}

		[Fact]
		public void Test04()
		{
			var vars = new Dictionary<string, decimal>();
			var str = "-(12.5-4)";
			var expr = new Expression(str);
			var res = expr.Evaluate(vars);
			Assert.Equal(-8.5m, res);
		}
		[Fact]
		public void Test05()
		{
			var vars = new Dictionary<string, decimal>();
			var str = "-12.5+4";
			var expr = new Expression(str);
			var res = expr.Evaluate(vars);
			Assert.Equal(-8.5m, res);
		}

		[Fact]
		public void Test06()
		{
			var vars = new Dictionary<string, decimal>();
			var str = "-(12.5+(4-4))";
			var expr = new Expression(str);
			var res = expr.Evaluate(vars);
			Assert.Equal(-12.5m, res);
		}
		[Fact]
		public void Test07()
		{
			var vars = new Dictionary<string, decimal>();
			var str = "-12.5-4-6";
			var expr = new Expression(str);
			var res = expr.Evaluate(vars);
			Assert.Equal(-22.5m, res);
		}
		[Fact]
		public void Test08()
		{
			var vars = new Dictionary<string, decimal>();
			var str = "-12.5-(6+4)";
			var expr = new Expression(str);
			var res = expr.Evaluate(vars);
			Assert.Equal(-22.5m, res);
		}
		[Fact]
		public void Test09()
		{
			var vars = new Dictionary<string, decimal>();
			var str = "-12.5+(-12.5-5)";
			var expr = new Expression(str);
			var res = expr.Evaluate(vars);
			Assert.Equal(-30m, res);
		}
		[Fact]
		public void Test10()
		{
			var vars = new Dictionary<string, decimal>();
			var str = "floor(12.5+4)";
			var expr = new Expression(str);
			var res = expr.Evaluate(vars);
			Assert.Equal(16m, res);
		}
		[Fact]
		public void Test11()
		{
			var vars = new Dictionary<string, decimal>();
			var str = "ceil(12.5+4)";
			var expr = new Expression(str);
			var res = expr.Evaluate(vars);
			Assert.Equal(17m, res);
		}
	}
}
