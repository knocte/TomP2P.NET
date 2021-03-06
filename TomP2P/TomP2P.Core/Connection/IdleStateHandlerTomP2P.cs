﻿using System;
using System.Runtime.CompilerServices;
using System.Threading;
using TomP2P.Core.Connection.Windows.Netty;
using TomP2P.Extensions;
using TomP2P.Extensions.Workaround;

namespace TomP2P.Core.Connection
{
    public class IdleStateHandlerTomP2P : BaseDuplexHandler
    {
        public int AllIdleTimeMillis { get; private set; }

        private readonly VolatileLong _lastReadTime = new VolatileLong(0);
        private readonly VolatileLong _lastWriteTime = new VolatileLong(0);

        // .NET-specific
        private ExecutorService _executor;
        private volatile CancellationTokenSource _cts;
        private readonly int _allIdleTimeSeconds; // for instance-cloning

        private volatile int _state; // 0 - none, 1 - initialized, 2- destroyed

        /// <summary>
        /// Creates a new instance firing IdleStateEvents.
        /// </summary>
        /// <param name="allIdleTimeSeconds">An IdleStateEvent whose state is AllIdle will be triggered
        /// when neither read nor write was performed for the specified period of time.
        /// Specify 0 to disable.</param>
        public IdleStateHandlerTomP2P(int allIdleTimeSeconds)
        {
            _allIdleTimeSeconds = allIdleTimeSeconds;
            if (allIdleTimeSeconds <= 0)
            {
                AllIdleTimeMillis = 0;
            }
            else if (allIdleTimeSeconds >= Int32.MaxValue)
            {
                AllIdleTimeMillis = Int32.MaxValue;
            }
            else
            {
                AllIdleTimeMillis = (int) TimeSpan.FromSeconds(allIdleTimeSeconds).TotalMilliseconds;
            }
        }

        /*public override void HandlerAdded(ChannelHandlerContext ctx)
        {
            if (ctx.Channel.IsActive)
            {
                Initialize(ctx);
            }
        }

        public override void HandlerRemoved(ChannelHandlerContext ctx)
        {
            Destroy();
        }*/

        public override void ChannelActive(ChannelHandlerContext ctx)
        {
            base.ChannelActive(ctx);
            Initialize(ctx);
        }

        public override void ChannelInactive(ChannelHandlerContext ctx)
        {
            base.ChannelInactive(ctx);
            Destroy();
        }

        public override void Read(ChannelHandlerContext ctx, object msg)
        {
            _lastReadTime.Set(Convenient.CurrentTimeMillis());
            //ctx.FireRead(msg);
        }

        public override void Write(ChannelHandlerContext ctx, object msg)
        {
            ctx.Channel.WriteCompleted += channel => _lastWriteTime.Set(Convenient.CurrentTimeMillis());
            //ctx.FireWrite(msg); // TODO needed?
        }

        private void Initialize(ChannelHandlerContext ctx)
        {
            switch (_state)
            {
                case 1:
                    return;
                case 2:
                    return;
            }
            _state = 1;

            // .NET-specific:
            if (_executor == null)
            {
                _executor = new ExecutorService();
            }
            var currentMillis = Convenient.CurrentTimeMillis();
            _lastReadTime.Set(currentMillis);
            _lastWriteTime.Set(currentMillis);

            if (AllIdleTimeMillis > 0)
            {
                _cts = _executor.Schedule(AllIdleTimeoutTask, ctx, AllIdleTimeMillis);
            }
        }

        private void AllIdleTimeoutTask(object state)
        {
            var ctx = state as ChannelHandlerContext;

            // .NET-specific: don't fire if session is already set to timed out
            if (ctx == null || !ctx.Channel.IsOpen || ctx.IsTimedOut)
            {
                return;
            }
            long currentTime = Convenient.CurrentTimeMillis();
            long lastIoTime = Math.Max(_lastReadTime.Get(), _lastWriteTime.Get());
            long nextDelay = (AllIdleTimeMillis - (currentTime - lastIoTime));
            if (nextDelay <= 0)
            {
                // both reader and writer are idle
                // --> set a new timeout and notify the callback
                //Logger.Debug("Both reader and writer are idle...");
                _cts = _executor.Schedule(AllIdleTimeoutTask, ctx, AllIdleTimeMillis);
                try
                {
                    ctx.FireUserEventTriggered(this);
                }
                catch (Exception ex)
                {
                    ctx.FireExceptionCaught(ex);
                }
            }
            else
            {
                // either read or write occurred before the timeout
                // --> set a new timeout with shorter delayMillis
                _cts = _executor.Schedule(AllIdleTimeoutTask, ctx, nextDelay);
            }
        }

        private void Destroy()
        {
            _state = 2;
            // .NET-specific:
            if (_cts != null)
            {
                _cts.Cancel();
                _cts = null;
            }
            if (_executor != null)
            {
                _executor.Shutdown();
            }
        }

        public override IChannelHandler CreateNewInstance()
        {
            return new IdleStateHandlerTomP2P(_allIdleTimeSeconds);
        }

        public override string ToString()
        {
            return String.Format("IdleStateHandlerTomP2P ({0})", RuntimeHelpers.GetHashCode(this));
        }
    }
}
