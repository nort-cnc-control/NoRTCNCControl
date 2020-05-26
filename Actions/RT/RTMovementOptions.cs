using System;

namespace Actions
{
    public class RTMovementOptions
    {
        public decimal FeedStart, FeedEnd, Feed;
        public decimal acceleration;
        public RTMovementOptions()
        {
            this.FeedStart = 0;
            this.FeedEnd = 0;
            this.Feed = 0;
            this.acceleration = 0;
        }
        public RTMovementOptions(decimal feed_start, decimal feed, decimal feed_end, decimal acceleration)
        {
            this.FeedStart = feed_start;
            this.FeedEnd = feed_end;
            this.Feed = feed;
            this.acceleration = acceleration;
        }

        public string Command => $"T{acceleration:0.00}P{FeedStart:0.00}F{Feed:0.00}L{FeedEnd:0.00}";
    }

}
