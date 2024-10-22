﻿namespace TailwindCSSIntellisense.Helpers;
internal class ImportantModiferHelper
{
    public static bool IsImportantModifier(string classText)
    {
        return classText.StartsWith("!") && !(classText.Length >= 2 && classText[1] == '!');
    }
}
