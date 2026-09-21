namespace LogicCuteGuy.UdonSharpInterfaceExample
{
    /// <summary>
    /// A source-defined interface handled by the custom UdonSharp compiler.
    /// </summary>
    public interface INumberOperation
    {
        int Apply(int value);
        int LastResult { get; }
    }
}
