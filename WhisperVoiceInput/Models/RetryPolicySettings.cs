using System;

namespace WhisperVoiceInput.Models
{
    /// <summary>
    /// Supervision strategy type for actors
    /// </summary>
    public enum SupervisionStrategyType
    {
        /// <summary>
        /// Standard OneForOne strategy with immediate restarts
        /// </summary>
        OneForOne,
        
        /// <summary>
        /// Backoff strategy with exponential delay
        /// </summary>
        Backoff
    }

    /// <summary>
    /// Settings for actor retry policies and supervision strategies.
    /// These settings are not configurable by the user through the UI
    /// but are defined in application configuration.
    /// </summary>
    public class RetryPolicySettings
    {
        /// <summary>
        /// Type of supervision strategy to use
        /// </summary>
        public SupervisionStrategyType StrategyType { get; set; } = SupervisionStrategyType.OneForOne;

        /// <summary>
        /// Maximum number of retries before giving up
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// Time window for counting retries (used in OneForOne strategy)
        /// </summary>
        public TimeSpan RetryTimeWindow { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Initial delay before retrying (used in Backoff strategy)
        /// </summary>
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Maximum delay between retries (used in Backoff strategy)
        /// </summary>
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Random factor for backoff strategy (0.0 - 1.0)
        /// </summary>
        public double RandomFactor { get; set; } = 0.2;
    }
}
