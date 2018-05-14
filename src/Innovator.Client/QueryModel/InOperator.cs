using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Innovator.Client.QueryModel
{
  public class InOperator : IOperator
  {
    public IExpression Left { get; set; }
    public ListExpression Right { get; set; }

    public int Precedence => (int)PrecedenceLevel.Comparison;

    public void Visit(IExpressionVisitor visitor)
    {
      visitor.Visit(this);
    }

    public override string ToString()
    {
      return this.ToSqlString();
    }
  }
}
