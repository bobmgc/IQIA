using System;

namespace IQIAIndicator.Engine.EntryTrigger;

public sealed class EntryTriggerPresentationEngine
{
    public EntryTriggerPresentation Process(EntryTriggerCandidate candidate)
    {
        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        var builder = new EntryTriggerPresentationBuilder();
        return builder.Build(candidate);
    }
}
