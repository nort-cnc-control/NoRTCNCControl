using System;
using ActionProgram;
using Actions;
using System.Collections.Generic;
using System.Linq;
using Config;

namespace Processor
{
    public class MoveOptimizer : IProcessor
    {
        private class MoveActionChain
        {
            public List<IRTMoveCommand> Actions;
            public MoveActionChain()
            {
                Actions = new List<IRTMoveCommand>();
            }
        }

        private MachineParameters config;
        public MoveOptimizer(MachineParameters config)
        {
            this.config = config;
        }

        private List<MoveActionChain> SplitChainByDirectionFlip(MoveActionChain origChain)
        {
            List<MoveActionChain> chains = new List<MoveActionChain>();
            MoveActionChain chain = new MoveActionChain();
            var actions = origChain.Actions;
            IRTMoveCommand prevCmd = null;
            foreach (var cmd in actions)
            {
                if (prevCmd != null)
                {
                    Vector3 dir_cur = cmd.DirStart;
                    Vector3 dir_prev = prevCmd.DirEnd;
                    if (dir_cur * dir_prev <= 1e-3)
                    {
                        chains.Add(chain);
                        chain = new MoveActionChain();
                    }
                }
                chain.Actions.Add(cmd);
                prevCmd = cmd;
            }
            if (chain.Actions.Count > 0)
            {
                chains.Add(chain);
            }
            return chains;
        }

        private double MaxFeed(double cosa, double maxLeap)
        {
            if (cosa > 1)
                cosa = 1;
            double deltaDir = Math.Sqrt(2 * (1-cosa));
            if (deltaDir < 1e-8)
                return Double.PositiveInfinity;
            return maxLeap / deltaDir;
        }

        #region physics
        // Real physics is contained here
        
        // Find intersections between acceleration and deceleration paths
        // x0 - position of path begin
        // feed0 - feed at begin
        // x1 - position of path end
        // feed1 - feed at end
        // Returns: position and feed of intersection point
        //
        //    ___________
        //    |         |
        // f  |     /\  |
        //    |    /  \ |
        //    |   /    \| f1
        //    |  /                      
        //    | /
        // f0 |/
        //
        //    x0    x  x1
        //
        // Note: actually f(x) is not a linear function! f(t) is.
        // But due to limitations of pseudo-graphics f(x) on image above is also drawed as linear.
        private (double x, double feed) AcceletationDecelerationIntersection(double x0, double feed0,
                                                                             double x1, double feed1,
                                                                             double acc)
        {
            //Console.WriteLine("x0 = {0}, x1 = {1}", x0, x1);
            if (x0 >= x1)
                throw new ArgumentOutOfRangeException("x0 must be lower than x1");
            double x = (x0 + x1)/2 + (feed1*feed1-feed0*feed0) / (4*acc);
            double f = Math.Sqrt(acc*(x1-x0) + (feed1*feed1+feed0*feed0)/2);
            return (x, f);
        }

        // Found initial feed of acceleration path from end of acceleration and end feed
        private double AccelerationInitialFeed(double x, double feed, double x0, double acc)
        {
            double D = feed*feed - 2*acc * (x-x0);
            if (D < 0)
                return 0;
            return Math.Sqrt(D);
        }

        // Found end feed of deceleration path from begin of deceleration and begin feed
        private double DecelerationInitialFeed(double x, double feed, double x1, double acc)
        {
            double D = feed*feed - 2*acc * (x1-x);
            if (D < 0)
                return 0;
            return Math.Sqrt(D);
        }
        #endregion

        private void OptimizeChain(MoveActionChain chain)
        {
            var actions = chain.Actions;
            int len = actions.Count;
            // Maximal feed at segment
            double[] maxFeedAtSegment = new double[len];
            // Distance of joints from chain begin
            double[] X = new double[len + 1];
            X[0] = 0;
            for (int i = 0; i < len; ++i)
            {
                maxFeedAtSegment[i] = actions[i].Options.Feed;
                X[i+1] = X[i] + actions[i].Length;
                //Console.WriteLine("X[{0}] = {1}", i+1, X[i+1]);
            }

            // Maximal feed at the ends of segments
            double[] maxFeedAtJoint = new double[len + 1];
            maxFeedAtJoint[0] = 0;
            maxFeedAtJoint[len] = 0;
            for (int i = 1; i < len; ++i)
            {
                int m1 = i-1;
                int m2 = i;
                Vector3 dirEnd   = actions[m1].DirEnd;
                Vector3 dirStart = actions[m2].DirStart;
                double mf = MaxFeed(dirEnd*dirStart, config.max_movement_leap);
                maxFeedAtJoint[m2] = Math.Min(mf, Math.Min(maxFeedAtSegment[m1], maxFeedAtSegment[m2]));
            }

            // Calculate feeds
            double[] feedAtSegment = new double[len];
            double[] feedAtJoint = new double[len+1];
            feedAtJoint[0] = 0;
            feedAtJoint[len] = 0;
            // Find minimal intersection point for segments
            for (int i = 0; i < maxFeedAtSegment.Length; ++i)
            {
                double xmin = Double.NaN;
                double fmin = Double.PositiveInfinity;
                
                // Find minimum from all pairs of acceleration and deceleration intersections
                for (int j = 0; j <= i; ++j)
                for (int k = i+1; k < maxFeedAtJoint.Length; ++k)
                {
                    double x0 = X[j];
                    double feed0 = maxFeedAtJoint[j];
                    double x1 = X[k];
                    double feed1 = maxFeedAtJoint[k];
                    
                    var intersection = AcceletationDecelerationIntersection(x0, feed0, x1, feed1, config.max_acceleration);
                    double feed = intersection.feed;
                    if (feed < fmin)
                    {
                        fmin = feed;
                        xmin = intersection.x;
                    }
                }

                // Fill segment begin, end and center feeds
                double segmentFeedStart;
                double segmentFeedEnd;
                double segmentFeed;
                if (xmin < X[i])
                {
                    segmentFeedStart = DecelerationInitialFeed(xmin, fmin, X[i], config.max_acceleration);
                    segmentFeedEnd   = DecelerationInitialFeed(xmin, fmin, X[i+1], config.max_acceleration);
                    segmentFeed      = segmentFeedStart;
                }
                else if (xmin > X[i+1])
                {
                    segmentFeedStart = AccelerationInitialFeed(xmin, fmin, X[i], config.max_acceleration);
                    segmentFeedEnd   = AccelerationInitialFeed(xmin, fmin, X[i+1], config.max_acceleration);
                    segmentFeed      = segmentFeedEnd;
                }
                else
                {
                    segmentFeedStart = AccelerationInitialFeed(xmin, fmin, X[i], config.max_acceleration); 
                    segmentFeedEnd   = DecelerationInitialFeed(xmin, fmin, X[i+1], config.max_acceleration);
                    segmentFeed      = fmin;
                }
                feedAtSegment[i] = Math.Min(segmentFeed, maxFeedAtSegment[i]);
                feedAtJoint[i]   = Math.Min(segmentFeedStart, feedAtSegment[i]);
                feedAtJoint[i+1] = Math.Min(segmentFeedEnd, feedAtSegment[i]);
            }

            // Fill actions feeds
            for (int i = 0; i < len; i++)
            {
                actions[i].Options.Feed = feedAtSegment[i];
                actions[i].Options.FeedStart = feedAtJoint[i];
                actions[i].Options.FeedEnd = feedAtJoint[i+1];
            }
        }

        public void ProcessProgram(ActionProgram.ActionProgram program)
        {
            List<MoveActionChain> chains = new List<MoveActionChain>();
            MoveActionChain chain = new MoveActionChain();
            var actions = program.Actions;
            foreach (var action in actions)
            {
                var ma = action.action as RTAction;
                if (ma == null || ma.RequireFinish || ma.Command as IRTMoveCommand == null)
                {
                    if (chain.Actions.Count > 0)
                    {
                        var subc = SplitChainByDirectionFlip(chain);
                        chains.AddRange(subc);
                        chain = new MoveActionChain();
                    }
                }
                else
                {
                    var cmd = ma.Command as IRTMoveCommand;
                    chain.Actions.Add(cmd);
                }
            }
            if (chain.Actions.Count > 0)
            {
                var subc = SplitChainByDirectionFlip(chain);
                chains.AddRange(subc);
            }

            foreach (var c in chains)
            {
                OptimizeChain(c);
            }
        }
    }
}
