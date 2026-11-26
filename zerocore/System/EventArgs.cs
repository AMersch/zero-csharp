namespace System
{
    // The base of the arguments an event passes, and the usual handler types.
    // Delegates here are single-target (no Delegate.Combine), so a class that
    // offers an event writes its own add and remove (lib/Timers.cs does).
    public class EventArgs
    {
        public static readonly EventArgs Empty = new EventArgs();
    }

    public delegate void EventHandler(object sender, EventArgs e);
    public delegate void EventHandler<TEventArgs>(object sender, TEventArgs e);
}
