using Lexico;

namespace Element.AST
{
    [WhitespaceSurrounded]
    public class Port
    {
        public Port() { }

        public Port(Identifier identifier, Type type)
        {
            Identifier = identifier;
            Type = type;
        }

        [field: Term] public Identifier Identifier { get; }
        [field: Optional] public Type Type { get; }

        public override string ToString() => Identifier;
    }
    
    public class PortList : ListOf<Port> { } // CallExpression looks like a list due to using brackets
}