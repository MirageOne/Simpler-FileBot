namespace SimplerFileBot.Core;

public sealed record RenameEntry(string SourcePath, string NewName)
{
    public string DestinationPath => Path.Combine(Path.GetDirectoryName(SourcePath)!, NewName);
}

public sealed record RenameMove(string From, string To);
public sealed record RenameOutcome(IReadOnlyList<RenameMove> Completed, Exception? Error);

public static class RenameService
{
    public static IReadOnlyDictionary<RenameEntry, string> Validate(IReadOnlyList<RenameEntry> entries)
    {
        var errors = new Dictionary<RenameEntry, string>();
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var error = FormatService.ValidateFileName(entry.NewName);
            if (error is not null) { errors[entry] = error; continue; }
            var source = Path.GetFullPath(entry.SourcePath);
            var destination = Path.GetFullPath(entry.DestinationPath);
            if (!File.Exists(source)) error = "The source file no longer exists.";
            if (error is null && !sources.Add(source)) error = "The source appears more than once.";
            if (error is null && !destinations.Add(destination)) error = "Another row has this destination.";
            if (error is null && !source.Equals(destination, StringComparison.OrdinalIgnoreCase) &&
                (File.Exists(destination) || Directory.Exists(destination)))
                error = "A file already exists at this destination.";
            if (error is not null) errors[entry] = error;
        }
        return errors;
    }

    public static RenameOutcome Execute(IReadOnlyList<RenameEntry> entries)
    {
        var errors = Validate(entries);
        if (errors.Count > 0) return new RenameOutcome([], new InvalidOperationException(errors.First().Value));
        var done = new List<RenameMove>();
        foreach (var entry in entries)
        {
            var source = Path.GetFullPath(entry.SourcePath);
            var destination = Path.GetFullPath(entry.DestinationPath);
            if (source.Equals(destination, StringComparison.Ordinal)) continue;
            try
            {
                // File.Move without overwrite fails if a destination appears after preflight.
                File.Move(source, destination);
                done.Add(new RenameMove(source, destination));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return new RenameOutcome(done, error);
            }
        }
        return new RenameOutcome(done, null);
    }

    public static RenameOutcome Undo(IReadOnlyList<RenameMove> moves)
    {
        var completed = new List<RenameMove>();
        foreach (var move in moves.Reverse())
        {
            if (File.Exists(move.From)) return new RenameOutcome(completed, new IOException($"Original path is occupied: {move.From}"));
            try
            {
                File.Move(move.To, move.From);
                completed.Add(new RenameMove(move.To, move.From));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return new RenameOutcome(completed, error);
            }
        }
        return new RenameOutcome(completed, null);
    }
}
