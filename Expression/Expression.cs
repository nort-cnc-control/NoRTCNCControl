using System;
using System.Collections.Generic;
using System.Linq;

namespace Expression
{
    public enum MathConstant
    {
        Pi,             // 3.1415926535
    }

    public enum MathFunction
    {
        Cos,            // cos(x)
        Sin,            // sin(x)
        Floor,          // floor(x)
        Ceil,           // ceil(x)
        Int,            // int(x)
    }

    public class Token
    {
        public enum TokenType
        {
            Number,         // decimal
            Operation,      // OperationToken
            Variable,       // #id
            Name,           // Named object
            Sequence,       // List of tokens
        }

        public enum OperationToken
        {
            Plus,           // +
            Minus,          // -
            LeftBracket,    // (
            RightBracket,   // )
            Multiply,       // *
            Divide,         // /
            Power,          // ^
        }

        public static int OperationTokenPriority(OperationToken token)
        {
            switch (token)
            {
                case OperationToken.Minus:
                case OperationToken.Plus:
                    return 0;
                case OperationToken.Multiply:
                case OperationToken.Divide:
                    return 1;
                case OperationToken.Power:
                    return 2;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public TokenType tokenType;
        public OperationToken operationToken;
        public string name;
        public decimal value;
        public string variableId;
        public List<Token> sequence = null;
        public Token argument = null;
    }

    public class Operation
    {
        public enum OperationType
        {
            Value,
            Add,
            Sub,
            Negative,
            Multiply,
            Divide,
            Power,
            Call,
        }

        public enum ArgumentType
        {
            Number,
            Constant,
            Variable,
            Function,
            Expression,
        }

        public OperationType operation;

        public object argument1;
        public ArgumentType argument1type;

        public object argument2;
        public ArgumentType argument2type;

        private decimal eval(object arg, ArgumentType type, IReadOnlyDictionary<string, decimal> vars)
        {
            switch (type)
            {
                case ArgumentType.Number:
                    {
                        return (arg as decimal?).Value;
                    }
                case ArgumentType.Variable:
                    {
                        string varid = arg as string;
                        return vars[varid];
                    }
                case ArgumentType.Expression:
                    {
                        var subExpr = arg as Operation;
                        return subExpr.Evaluate(vars);
                    }
                case ArgumentType.Constant:
                    {
                        var c = (arg as MathConstant?).Value;
                        switch (c)
                        {
                            case MathConstant.Pi:
                                return (decimal)Math.PI;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                case ArgumentType.Function:
                    throw new ArgumentOutOfRangeException();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private decimal call(MathFunction fun, decimal argument)
        {
            switch (fun)
            {
                case MathFunction.Cos:
                    return (decimal)Math.Cos((double)argument);
                case MathFunction.Sin:
                    return (decimal)Math.Sin((double)argument);
                case MathFunction.Floor:
                    return (decimal)Math.Floor((double)argument);
                case MathFunction.Ceil:
                    return (decimal)Math.Ceiling((double)argument);
                case MathFunction.Int:
                    return (int)argument;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public decimal Evaluate(IReadOnlyDictionary<string, decimal> vars)
        {
            switch (operation)
            {
                case OperationType.Value:
                    return eval(argument1, argument1type, vars);
                case OperationType.Add:
                    return eval(argument1, argument1type, vars) + eval(argument2, argument2type, vars);
                case OperationType.Sub:
                    return eval(argument1, argument1type, vars) - eval(argument2, argument2type, vars);
                case OperationType.Negative:
                    return -eval(argument1, argument1type, vars);
                case OperationType.Multiply:
                    return eval(argument1, argument1type, vars) * eval(argument2, argument2type, vars);
                case OperationType.Divide:
                    return eval(argument1, argument1type, vars) / eval(argument2, argument2type, vars);
                case OperationType.Power:
                    return (decimal)Math.Pow((double)eval(argument1, argument1type, vars), (double)eval(argument2, argument2type, vars));
                case OperationType.Call:
                    {
                        if (argument1 is MathFunction?)
                        {
                            var fun = (argument1 as MathFunction?).Value;
                            return call(fun, eval(argument2, argument2type, vars));
                        }
                        else
                        {
                            throw new ArgumentException();
                        }
                    }
                default:
                    throw new InvalidOperationException();
            }
        }
    }

    public class Expression
    {
        private Operation operation;
        private readonly IReadOnlyList<Token> tokens;

        private IReadOnlyList<Token> Tokenize(string expr)
        {
            expr = expr.ToLower();
            List<Token> list = new List<Token>();
            int len = expr.Length;
            string name = "";
            bool name_processing = false;
            bool varid_processing = false;
            bool number_processing = false;

            void tokenizer_finish_name()
            {
                if (!name_processing && !varid_processing && !number_processing)
                    return;
                if (varid_processing)
                {
                    list.Add(new Token { tokenType = Token.TokenType.Variable, variableId = name });
                }
                else if (name_processing)
                {
                    list.Add(new Token { tokenType = Token.TokenType.Name, name = name });
                }
                else if (number_processing)
                {
                    decimal num = decimal.Parse(name);
                    list.Add(new Token { tokenType = Token.TokenType.Number, value = num });
                }

                name = "";
                name_processing = false;
                varid_processing = false;
                number_processing = false;
            }

            for (int i = 0; i < len; i++)
            {
                char c = expr[i];

                if (varid_processing)
                {
                    if (char.IsDigit(c) || char.IsLetter(c) || c == '_')
                        name += c;
                    else
                        tokenizer_finish_name();
                }

                if (name_processing)
                {
                    if (char.IsLetter(c) || char.IsDigit(c))
                        name += c;
                    else
                        tokenizer_finish_name();
                }

                if (number_processing)
                {
                    if (char.IsDigit(c) || c == '.')
                        name += c;
                    else
                        tokenizer_finish_name();
                }

                if (c == '*' || c == '/' || c == '+' || c == '-' || c == '(' || c == ')' || c == '^')
                {
                    Token.OperationToken tt;
                    switch (c)
                    {
                        case '*':
                            tt = Token.OperationToken.Multiply;
                            break;
                        case '/':
                            tt = Token.OperationToken.Divide;
                            break;
                        case '+':
                            tt = Token.OperationToken.Plus;
                            break;
                        case '-':
                            tt = Token.OperationToken.Minus;
                            break;
                        case '^':
                            tt = Token.OperationToken.Power;
                            break;
                        case '(':
                            tt = Token.OperationToken.LeftBracket;
                            break;
                        case ')':
                            tt = Token.OperationToken.RightBracket;
                            break;
                        default:
                            throw new InvalidOperationException();
                    }
                    list.Add(new Token { tokenType = Token.TokenType.Operation, operationToken = tt });
                }
                else if (c == '#')
                {
                    if (!varid_processing && !name_processing && !number_processing)
                    {
                        name = "";
                        varid_processing = true;
                    }
                }
                else if (char.IsLetter(c))
                {
                    if (!name_processing && !varid_processing && !number_processing)
                    {
                        name = c.ToString();
                        name_processing = true;
                    }
                }
                else if (char.IsDigit(c))
                {
                    if (!name_processing && !number_processing && !varid_processing)
                    {
                        name = c.ToString();
                        number_processing = true;
                    }
                }
            }

            if (name_processing || varid_processing || number_processing)
                tokenizer_finish_name();

            return list;
        }

        public Token Sequence(IReadOnlyList<Token> tokens)
        {
            Token root = new Token
            {
                tokenType = Token.TokenType.Sequence,
                sequence = new List<Token>(),
            };

            int brackets = 0;
            int begin = 0;
            int end = 0;
            bool ignore = false;
            for (int i = 0; i < tokens.Count; i++)
            {
                ignore = false;
                var token = tokens[i];
                if (token.tokenType == Token.TokenType.Operation)
                {
                    if (token.operationToken == Token.OperationToken.LeftBracket)
                    {
                        ignore = true;
                        brackets++;
                        if (brackets == 1)
                        {
                            begin = i+1;
                        }
                    }

                    else if (token.operationToken == Token.OperationToken.RightBracket)
                    {
                        ignore = true;
                        if (brackets == 1)
                        {
                            end = i-1;
                            var sublist = tokens.ToList().GetRange(begin, end - begin + 1);
                            root.sequence.Add(Sequence(sublist));
                        }
                        brackets--;
                    }
                }

                if (brackets == 0 && !ignore)
                {
                    root.sequence.Add(token);
                }
            }
            // Remove redundant sequencing
            while (root.tokenType == Token.TokenType.Sequence && root.sequence.Count == 1)
                root = root.sequence[0];
        
            return root;
        }

        public Token DetectCalls(Token root)
        {
            if (root.tokenType != Token.TokenType.Sequence)
                return root;

            Token newroot = new Token
            {
                tokenType = Token.TokenType.Sequence,
                sequence = new List<Token>(),
            };
            Token prev = null;
            for (int i = 0; i < root.sequence.Count; i++)
            {
                var token = root.sequence[i];
                if (token.tokenType == Token.TokenType.Sequence)
                {
                    token = DetectCalls(token);
                }

                if (prev != null && prev.tokenType == Token.TokenType.Name &&
                    (	
						token.tokenType == Token.TokenType.Sequence ||
						token.tokenType == Token.TokenType.Number ||
						token.tokenType == Token.TokenType.Variable ||
						token.tokenType == Token.TokenType.Name
					))
                {
                    prev.argument = token;
                    newroot.sequence.Add(prev);
                    prev = null;
                }
                else
                {
                    if (prev != null)
                        newroot.sequence.Add(prev);
                    prev = token;
                }
            }
            if (prev != null)
                newroot.sequence.Add(prev);
            while (newroot.tokenType == Token.TokenType.Sequence &&
                   newroot.sequence.Count == 1)
                newroot = newroot.sequence[0];
            return newroot;
        }

        private Token PriorityConstruct(Token root)
        {
            if (root.tokenType == Token.TokenType.Sequence)
            {
                List<Token> seq = new List<Token>();
                foreach (var token in root.sequence)
                {
                    seq.Add(PriorityConstruct(token));
                }

                if (seq.Count == 1)
                    return seq[0];

                if (seq[0].tokenType == Token.TokenType.Operation && seq[0].operationToken == Token.OperationToken.Plus)
                {
                    // Leading '+'. Drop
                    seq = seq.GetRange(1, seq.Count - 1);
                }

                while (seq.Count > 3)
                {
                    List<int> operators = new List<int>();
                    for (int i = 0; i < seq.Count; i++)
                    {
                        if (seq[i].tokenType == Token.TokenType.Operation)
                        {
                            Token arg1 = null;
                            if (i - 1 >= 0)
                                arg1 = seq[i - 1];
                            Token arg2 = null;
                            if (i + 1 < seq.Count)
                                arg2 = seq[i + 1];

                            if (arg2 == null)
                                throw new ArgumentOutOfRangeException();
                            if (arg1 != null && arg1.tokenType == Token.TokenType.Operation)
                                throw new ArgumentOutOfRangeException();
                            if (arg2.tokenType == Token.TokenType.Operation)
                                throw new ArgumentOutOfRangeException();

                            operators.Add(i);
                        }
                    }

                    // Find maximal priority of operators in sequence
                    int maxpriority = int.MinValue;
                    for (int i = 0; i < operators.Count; i++)
                    {
                        int pr = Token.OperationTokenPriority(seq[operators[i]].operationToken);
                        if (pr > maxpriority)
                            maxpriority = pr;
                    }

                    // Find first subsequence of max priority operators
                    int begin = -1;
                    int end = -1;
                    for (int i = 0; i < operators.Count; i++)
                    {
                        int pr = Token.OperationTokenPriority(seq[operators[i]].operationToken);
                        if (pr == maxpriority && begin == -1)
                        {
                            begin = i;
                        }
                        if (pr < maxpriority && begin != -1 && end == -1)
                        {
                            end = i - 1;
                        }
                    }
                    if (end == -1)
                        end = operators.Count - 1;

                    if (begin == 0 && end == operators.Count - 1)
                    {
                        end = operators.Count - 2;
                    }

                    // Move subsequence to sequence token
                    int sbegin = Math.Max(operators[begin] - 1, 0);
                    int send = operators[end] + 1;

                    Token subtoken = new Token
                    {
                        tokenType = Token.TokenType.Sequence,
                        sequence = seq.GetRange(sbegin, send + 1 - sbegin),
                    };

                    subtoken = PriorityConstruct(subtoken);

                    List<Token> head = seq.GetRange(0, sbegin);
                    List<Token> tail = seq.GetRange(send + 1, seq.Count - (send + 1));

                    seq = head;
                    seq.Add(subtoken);
                    seq.AddRange(tail);
                }
                root.sequence = seq;
                return root;
            }
            else if (root.tokenType == Token.TokenType.Name && root.argument != null)
            {
                root.argument = PriorityConstruct(root.argument);
                return root;
            }
            else
            {
                return root;
            }
        }

        private (object argument, Operation.ArgumentType type) MakeArgument(Token token)
        {
            switch (token.tokenType)
            {
                case Token.TokenType.Number:
                    return ((decimal?)token.value, Operation.ArgumentType.Number);
                case Token.TokenType.Variable:
                    return (token.variableId, Operation.ArgumentType.Variable);
                case Token.TokenType.Name:
                    {
                        if (token.argument == null)
                        {
                            switch (token.name)
                            {
                                case "pi":
                                    return ((MathConstant?)MathConstant.Pi, Operation.ArgumentType.Constant);
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                        }
                        else
                        {
                            MathFunction fun;
                            switch (token.name)
                            {
                                case "cos":
                                    fun = MathFunction.Cos;
                                    break;
                                case "sin":
                                    fun = MathFunction.Sin;
                                    break;
                                case "floor":
                                    fun = MathFunction.Floor;
                                    break;
                                case "ceil":
                                    fun = MathFunction.Ceil;
                                    break;
                                case "int":
                                    fun = MathFunction.Int;
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                            object arg;
                            Operation.ArgumentType type;
                            if (token.argument.tokenType == Token.TokenType.Sequence)
                            {
                                arg = BuildOperation(token.argument);
                                type = Operation.ArgumentType.Expression;
                            }
                            else
                            {
                                (arg, type) = MakeArgument(token.argument);
                            }
                            Operation op = new Operation
                            {
                                operation = Operation.OperationType.Call,
                                argument1 = fun,
                                argument1type = Operation.ArgumentType.Function,
                                argument2 = arg,
                                argument2type = type,
                            };
                            return (op, Operation.ArgumentType.Expression);
                        }
                    }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private Operation BuildOperation(Token root)
        {
            if (root.tokenType == Token.TokenType.Sequence)
            {
                var seq = root.sequence;
                if (seq.Count == 0)
                {
                    throw new ArgumentOutOfRangeException();
                }
                if (seq.Count == 1)
                {
                    var (arg, type) = MakeArgument(seq[0]);
                    if (type == Operation.ArgumentType.Expression)
                        return arg as Operation;

                    Operation op = new Operation
                    {
                        operation = Operation.OperationType.Value,
                        argument1 = arg,
                        argument1type = type,
                    };
                    return op;
                }
                if (seq.Count == 2)
                {
                    if (seq[0].tokenType != Token.TokenType.Operation || seq[0].operationToken != Token.OperationToken.Minus)
                        throw new ArgumentOutOfRangeException();

                    object arg;
                    Operation.ArgumentType type;
                    if (seq[1].tokenType == Token.TokenType.Sequence)
                    {
                        arg = BuildOperation(seq[1]);
                        type = Operation.ArgumentType.Expression;
                    }
                    else
                    {
                        (arg, type) = MakeArgument(seq[1]);
                    }

                    Operation op = new Operation
                    {
                        operation = Operation.OperationType.Negative,
                        argument1 = arg,
                        argument1type = type,
                    };
                    return op;
                }

                if (seq.Count == 3)
                {
                    if (seq[1].tokenType != Token.TokenType.Operation)
                        throw new ArgumentOutOfRangeException();

                    object arg1, arg2;
                    Operation.ArgumentType type1, type2;
                    if (seq[0].tokenType == Token.TokenType.Sequence)
                    {
                        arg1 = BuildOperation(seq[0]);
                        type1 = Operation.ArgumentType.Expression;
                    }
                    else
                    {
                        (arg1, type1) = MakeArgument(seq[0]);
                    }

                    if (seq[2].tokenType == Token.TokenType.Sequence)
                    {
                        arg2 = BuildOperation(seq[2]);
                        type2 = Operation.ArgumentType.Expression;
                    }
                    else
                    {
                        (arg2, type2) = MakeArgument(seq[2]);
                    }

                    Operation op = new Operation
                    {
                        argument1 = arg1,
                        argument1type = type1,
                        argument2 = arg2,
                        argument2type = type2,
                    };

                    switch (seq[1].operationToken)
                    {
                        case Token.OperationToken.Divide:
                            op.operation = Operation.OperationType.Divide;
                            break;
                        case Token.OperationToken.Multiply:
                            op.operation = Operation.OperationType.Multiply;
                            break;
                        case Token.OperationToken.Plus:
                            op.operation = Operation.OperationType.Add;
                            break;
                        case Token.OperationToken.Minus:
                            op.operation = Operation.OperationType.Sub;
                            break;
                        case Token.OperationToken.Power:
                            op.operation = Operation.OperationType.Power;
                            break;
                    }
                    return op;
                }

                throw new ArgumentOutOfRangeException();
            }
            else
            {
                var (arg, type) = MakeArgument(root);
                if (type != Operation.ArgumentType.Expression)
                {
                    Operation op = new Operation
                    {
                        operation = Operation.OperationType.Value,
                        argument1 = arg,
                        argument1type = type,
                    };
                    return op;
                }
                else
                {
                    return arg as Operation;
                }
            }
        }

        public Expression(string expr)
        {
            tokens = Tokenize(expr);
            var root = Sequence(tokens);
            root = DetectCalls(root);
            root = PriorityConstruct(root);
            operation = BuildOperation(root);
        }

        public decimal Evaluate(IReadOnlyDictionary<string, decimal> vars)
        {
            return operation.Evaluate(vars);
        }
    }
}
