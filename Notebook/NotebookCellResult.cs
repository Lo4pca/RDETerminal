using System;

namespace RDETerminal.Notebook;

public sealed class NotebookCellResult
{
    public bool Success { get; set; }
    public string Output { get; set; }
    public string Error { get; set; }
    public object ReturnValue { get; set; }

    public string ToDisplayString()
    {
        if (!Success)
        {
            return string.IsNullOrEmpty(Error) ? "(error)" : Error;
        }

        string printed = string.IsNullOrEmpty(Output) ? string.Empty : Output;
        string returned = ReturnValue == null ? "(null)" : ReturnValue.ToString();

        if (string.IsNullOrEmpty(printed))
        {
            return returned;
        }

        if (ReturnValue == null)
        {
            return printed;
        }

        return printed + Environment.NewLine + "=> " + returned;
    }
}