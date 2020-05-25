using System;

namespace Actions
{
    public class RTMovementOptions
    {
        public double FeedStart, FeedEnd, Feed;
        public double acceleration;
        public RTMovementOptions()
        {
            this.FeedStart = 0;
            this.FeedEnd = 0;
            this.Feed = 0;
            this.acceleration = 0;
        }
        public RTMovementOptions(double feed_start, double feed, double feed_end, double acceleration)
        {
            this.FeedStart = feed_start;
            this.FeedEnd = feed_end;
            this.Feed = feed;
            this.acceleration = acceleration;
        }

        public string Command => $"T{acceleration}P{FeedStart}F{Feed}L{FeedEnd}";
    }
}
