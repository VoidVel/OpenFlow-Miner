namespace OpenFlowMiner.Core.Domain;

/// <summary>
/// A candidate bottleneck: an activity whose average time-until-the-next-event is
/// disproportionately long — a starting signal for where delay concentrates.
/// </summary>
/// <param name="Activity">The activity delay is measured from.</param>
/// <param name="AverageTimeToNext">Mean elapsed time from this activity to the following one.</param>
/// <param name="Observations">How many transitions out of this activity were measured.</param>
public sealed record Bottleneck(string Activity, TimeSpan AverageTimeToNext, int Observations);
