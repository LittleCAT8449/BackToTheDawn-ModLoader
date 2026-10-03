namespace BackToTheDawn.PhoneAPI;

internal static class PhoneDialogueGraph
{
    internal static string? Validate(IReadOnlyList<PhoneDialogueLine> lines)
    {
        if (lines.Count == 0)
        {
            return "A phone conversation must contain at least one line.";
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (line is null || string.IsNullOrWhiteSpace(line.Speaker) || line.Text is null ||
                !Enum.IsDefined(typeof(PhoneDialogueSpeakerType), line.SpeakerType) || line.Options is null)
            {
                return "Every phone line needs a speaker, text, valid speaker type and an option list.";
            }
            if (line.Id is not null && (string.IsNullOrWhiteSpace(line.Id) || !ids.Add(line.Id)))
            {
                return $"Phone line IDs must be nonempty and unique: '{line.Id}'.";
            }
            if (line.EndCall && (line.NextLineId is not null || line.Options.Count > 0))
            {
                return "A line that ends the call cannot also have a destination or options.";
            }
            var optionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var option in line.Options)
            {
                if (option is null || string.IsNullOrWhiteSpace(option.Id) ||
                    string.IsNullOrWhiteSpace(option.Text) || !optionIds.Add(option.Id))
                {
                    return "Each option needs nonempty text and an ID unique within its line.";
                }
                if (option.EndCall && option.NextLineId is not null)
                {
                    return "An option that ends the call cannot also have a destination.";
                }
            }
        }

        foreach (var line in lines)
        {
            if (line.NextLineId is not null && !ids.Contains(line.NextLineId))
            {
                return $"Unknown phone line destination '{line.NextLineId}'.";
            }
            foreach (var option in line.Options)
            {
                if (option.NextLineId is not null && !ids.Contains(option.NextLineId))
                {
                    return $"Unknown phone option destination '{option.NextLineId}'.";
                }
            }
        }
        return null;
    }

    internal static PhoneDialogueLine[] Snapshot(IReadOnlyList<PhoneDialogueLine> lines) =>
        lines.Select(line => line with { Options = line.Options.ToArray() }).ToArray();

    // -1 represents the end of the call. Explicit destinations can point forward
    // or backward; this supports both branches and returning to a service menu.
    internal static int Next(IReadOnlyList<PhoneDialogueLine> lines, int index, PhoneDialogueOption? option = null)
    {
        var line = lines[index];
        if (line.EndCall || option?.EndCall == true)
        {
            return -1;
        }
        var destination = option?.NextLineId ?? line.NextLineId;
        if (destination is not null)
        {
            for (var target = 0; target < lines.Count; target++)
            {
                if (lines[target].Id == destination)
                {
                    return target;
                }
            }
            throw new InvalidOperationException($"Phone line destination '{destination}' no longer exists.");
        }
        return index + 1 < lines.Count ? index + 1 : -1;
    }
}
