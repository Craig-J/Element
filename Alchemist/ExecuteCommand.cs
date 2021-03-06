namespace Alchemist
{
	using System.Collections.Generic;
	using System.Linq;
	using CommandLine;
	using Element;

	[Verb("execute", HelpText = "Compile and execute an element function with arguments, printing the results to standard out.")]
	internal class ExecuteCommand : BaseCommand
	{
		[Option('f', "function", Required = true, HelpText = "Function to execute.")]
		public string Function { get; set; }

		[Option('a', "arguments", Required = false, HelpText = "Arguments to execute the function with as a flattened array of floats. Must be same length as function expects.")]
		public IEnumerable<float> Arguments { get; set; }

		protected override CompilationContext _compilationContext { get; } = new CompilationContext();

		protected override int ExecuteImplementation()
		{
			var function = _sourceContext.GlobalScope.GetFunction(Function, _compilationContext);
			if (function == null)
			{
				Alchemist.LogError($"Could not find function \"{Function}\".");
				return 1;
			}

			var output = function.EvaluateAndSerialize(Arguments.ToArray(), _compilationContext);
			var asString = string.Join(", ", output).Replace("∞", "Infinity");
			Alchemist.Log(asString);
			return 0;
		}
	}
}