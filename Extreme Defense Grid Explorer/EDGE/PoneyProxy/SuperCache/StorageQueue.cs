namespace PonyProxy.SuperCache
{
    using System.Collections.Generic;
    using System.Threading;

    internal class StorageQueue<T>
    {
        private readonly object gate = new object();
        private readonly int maxCapacity;
        private readonly Queue<T> queue = new Queue<T>();

        public StorageQueue(int maxCapacity)
        {
            this.maxCapacity = maxCapacity;
        }

        public int Count
        {
            get
            {
                lock (gate)
                {
                    return queue.Count;
                }
            }
        }

        public void Enqueue(T item)
        {
            lock (gate)
            {
                while (queue.Count >= maxCapacity)
                {
                    Monitor.Wait(gate);
                }

                queue.Enqueue(item);

                if (queue.Count == 1)
                {
                    Monitor.PulseAll(gate);
                }
            }
        }

        public T Dequeue()
        {
            lock (gate)
            {
                while (queue.Count == 0)
                {
                    Monitor.Wait(gate);
                }

                T item = queue.Dequeue();

                if (queue.Count == maxCapacity - 1)
                {
                    Monitor.PulseAll(gate);
                }

                return item;
            }
        }
    }
}
