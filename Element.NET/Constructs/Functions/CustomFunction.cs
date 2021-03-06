namespace Element
{
	using System;
	using System.Linq;
	using System.Collections.Generic;
	using Eto.Parse;

	/// <summary>
	/// A user-defined Function (which may or may not have a body!)
	/// </summary>
	internal class CustomFunction : IntrospectionBase, IScope, IDebuggable, INamedFunction
	{
		public override string ToString() => $"{Parent}.{Name}";

		public CustomFunction(IScope parent, Match ast, CompilationStack? stack, CompilationContext context, string source)
			: base(parent, ast, context, source)
		{
			_capturedCompilationStack = stack;
			_astBody = ast[ElementAST.FunctionBody];
			_astAssign = ast[ElementAST.AssignmentStatement];
			if (_astBody)
			{
				_drivers = ast[ElementAST.FunctionBody].Matches.ToDictionary(m => m[ElementAST.FunctionName].Text, m => m);

				// If there's no inputs or outputs, this is a namespace
				IsNamespace = (Ast[ElementAST.FunctionInputs] ? false : true);
				IsClass = IsNamespace && (Ast[ElementAST.FunctionOutputs] ? true : false);

				if (IsNamespace)
				{
					NamespaceMembers = _drivers.Keys.ToArray();
				}
			}
		}

		public bool IsNamespace { get; }
		public bool IsClass { get; }

		public string[] NamespaceMembers { get; } = Array.Empty<string>();

		// Cache some values for performance (ast[foo] is *slow*!)
		private readonly Match _astBody;
		private readonly Match _astAssign;
		private readonly Dictionary<string, Match> _drivers;

		// Capture the stack used when compiling this function to retain access to expressions defined in outer scopes if they are referenced locally
		private readonly CompilationStack? _capturedCompilationStack;

		/// <summary>
		/// Compiles an expression 'list' (such as chained calls or member accesses)
		/// </summary>
		private IFunction CompileExpressionList(Match list, CompilationStack stack, CompilationContext context) =>
			list.Matches.Count == 0
				? CompileExpression(null, list, stack, context)
				: list.Matches.Aggregate(default(IFunction), (current, exprAst) => CompileExpression(current, exprAst, stack, context));

		private CallSite MakeCallSite(Match match)
		{
			var text = ((Eto.Parse.Scanners.StringScanner)match.Scanner).Value;
			var line = 1;
			var column = 1;
			var index = match.Index;
			for (var i = 0; i < index; i++)
			{
				if (text[i] == '\r') {
					line++;
					column = 1;
					if (i+1 < text.Length && text[i+1] == '\n') {
						i++;
					}
				}
				else if (text[i] == '\n') {
					line++;
					column = 1;
				}
				else if (text[i] == '\t') {
					column += 4;
				}
				else
				{
					column++;
				}
			}
			
			return new CallSite(this, Source, line, column);
		}

		/// <summary>
		/// Compiles a single expression (e.g. part of an expression list)
		/// </summary>
		private IFunction CompileExpression(IFunction? previous, Match exprAst, CompilationStack stack, CompilationContext context)
		{
			switch (exprAst.Name)
			{
				case ElementAST.NumberExpression:
					return new Constant((float)exprAst.Value);
				case ElementAST.VariableExpression:
					context.Push(MakeCallSite(exprAst));
					var variable = CompileFunction(exprAst.Text, stack, context);
					context.Pop();
					return variable;
				case ElementAST.SubExpression:
					return previous.Call(exprAst[ElementAST.SubExpressionName].Text, context, MakeCallSite(exprAst));
				case ElementAST.CallExpression:
					// This is called a *lot*, so try to make it high performance:
					var args = exprAst[ElementAST.CallArguments].Matches;
					var argList = new IFunction[args.Count];
					for (var i = 0; i < argList.Length; i++)
					{
						argList[i] = CompileExpressionList(args[i], stack, context);
					}

					return previous.Call(argList, context, MakeCallSite(exprAst));
				default:
					return context.LogError($"Unknown expression {exprAst}");
			}
		}

		public IEnumerable<string> DriverNames => _drivers?.Keys ?? (IEnumerable<string>)new[] {"return"};
		public string[] IntermediateValues => Inputs.Select(i => i.Name).Concat(DriverNames).ToArray();

		public string DebugName => Name;

		public IFunction CompileFunction(string name, CompilationStack stack, CompilationContext context)
		{
			// First try to get a cached/inputted value...
			if (!stack.GetLocal(name, out var value))
			{
				// If that fails, look in the list of drivers:
				if (_drivers != null)
				{
					if (_drivers.TryGetValue(name, out var statement))
					{
						if (statement[ElementAST.TypeStatement]) // Check if this statement is a type declaration
						{
							var type = FindType(name, context);
							value = new Constructor(type, context);
						}
						else
						{
							value = new CustomFunction(this, statement, stack, context, Source);
						}

						stack.Add(name, value);
					}
				}
				// If there's no driver list (i.e. during an assignment), then the only output is 'return'
				// Since there's no other drivers we can assume
				else if (name == "return")
				{
					value = CompileExpressionList(_astAssign, stack, context);
					stack.Add(name, value);
				}
			}

			// Failing the above, try to find the value including parents in the stack
			// if we still cannot find a value, try using the captured compilation stack
			if (value == null && !stack.Get(name, out value) && _capturedCompilationStack != null)
			{
				value = Parent.CompileFunction(name, _capturedCompilationStack, context);
			}

			if (value == null)
			{
				return context.LogError("ELE0007", name);
			}

			return value.ResolveReturns(context, null); // TODO: Keep variable information here?
		}

		private readonly Dictionary<string, INamedType> _types = new Dictionary<string, INamedType>();

		public INamedType FindType(string name, CompilationContext context)
		{
			if (_types.TryGetValue(name, out var type))
			{
				return type;
			}

			Match typeAst = null;
			_drivers?.TryGetValue(name, out typeAst);
			if (typeAst != null && (typeAst[ElementAST.TypeStatement] ? true : false))
			{
				_types.Add(name, type = new CustomType(this, typeAst, context, Source));
				return type;
			}

			return Parent.FindType(name, context);
		}

		// If this function has no inputs, we can cache *all* our values here for all time
		// (well, the lifetime of this object anyway.)
		// NB this doesn't just include 'constants', local functions with no inputs can take advantage too!
		private CompilationStack _cache;

		public override IFunction CallInternal(IFunction[] arguments, string output, CompilationContext context)
		{
			if (IsNamespace && arguments.Length == 0)
			{
				return CompileIntermediate(arguments, output, context);
			}
			else if (IsNamespace && !IsClass)
			{
				return context.LogError($"This is a Namespace, so it has no constructor");
			}

			if (IsClass
				&& arguments.Length == Inputs.Length
				&& Outputs.All(p => p.Name != output)
				&& NamespaceMembers.Contains(output))
			{
				var memberFunction = CompileIntermediate(Array.Empty<IFunction>(), output, context);
				if (memberFunction.Inputs.Length > 0 && memberFunction.Inputs[0].Name == "this") {
					var classInstance = this.Call(arguments, context);
					return classInstance.AsMethod(memberFunction, MakeCallSite(Ast), context);
				}
			}

			context.Push(MakeCallSite(Ast));
			if (this.CheckArguments(arguments, output, context) != null)
			{				
				context.Pop();
				return Error.Instance;
			}

			context.Pop();

			if (IsClass)
			{
				return arguments[Array.FindIndex(Outputs, p => p.Name == output)];
			}

			var outputPort = Outputs.First(p => p.Name == output);
			var outValue = CompileIntermediate(arguments, output, context);
			var success = outputPort.Type.SatisfiedBy(outValue, context);
			return success switch
			{
				false => context.LogError("ELE0008", $"Output `{outputPort}` was not satisfied by its value `{outValue}` (See previous errors)"),
				null => Error.Instance,
				_ => outValue
			};
		}

		public IFunction CompileIntermediate(IFunction[] arguments, string name, CompilationContext context)
		{
			CompilationStack stack;
			if (IsNamespace || Inputs.Length == 0)
			{
				stack = _cache ??= _capturedCompilationStack?.Push() ?? new CompilationStack();
			}
			else
			{
				stack = _capturedCompilationStack?.Push() ?? new CompilationStack();
			}

			var inputs = Inputs;
			for (var i = 0; i < inputs.Length; i++)
			{
				stack.Add(inputs[i].Name, arguments[i]);
			}

			var output = CompileFunction(name, stack, context);
			return output;
		}
	}
}