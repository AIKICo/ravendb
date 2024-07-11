using Sparrow.Json.Parsing;

namespace Raven.Client.Documents.Operations.Refresh
{
    public class RefreshConfiguration : IDynamicJson
    {
        public bool Disabled { get; set; }

        public long? RefreshFrequencyInSec { get; set; }

        public long? MaxItemsToProcess { get; set; }

        protected bool Equals(RefreshConfiguration other)
        {
            return Disabled == other.Disabled && RefreshFrequencyInSec == other.RefreshFrequencyInSec && MaxItemsToProcess == other.MaxItemsToProcess;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (obj.GetType() != this.GetType())
                return false;
            return Equals((RefreshConfiguration)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Disabled.GetHashCode();
                hashCode = (hashCode * 397) ^ (RefreshFrequencyInSec?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (MaxItemsToProcess?.GetHashCode() ?? 0);
                return hashCode;
            }
        }

        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue
            {
                [nameof(Disabled)] = Disabled,
                [nameof(RefreshFrequencyInSec)] = RefreshFrequencyInSec,
                [nameof(MaxItemsToProcess)] = MaxItemsToProcess
            };
        }
    }
}
