﻿using Streamiz.Kafka.Net.Crosscutting;
using Streamiz.Kafka.Net.SerDes;
using Streamiz.Kafka.Net.State.Logging;
using Streamiz.Kafka.Net.State.Metered;
using Streamiz.Kafka.Net.State.Supplier;

namespace Streamiz.Kafka.Net.State
{
    public class WindowStoreBuilder<K, V>
        : AbstractStoreBuilder<K, V, IWindowStore<K, V>>
    {
        private readonly IWindowBytesStoreSupplier supplier;

        public WindowStoreBuilder(IWindowBytesStoreSupplier supplier, ISerDes<K> keySerde, ISerDes<V> valueSerde) 
            : base(supplier.Name, keySerde, valueSerde)
        {
            this.supplier = supplier;
        }

        public override bool IsWindowStore => true;

        public override long RetentionMs => supplier.Retention;

        public override IWindowStore<K, V> Build()
        {
            var store = supplier.Get();
            
            return new MeteredWindowStore<K, V>(
                WrapLogging(store),
                supplier.WindowSize.Value,
                keySerdes,
                valueSerdes,
                supplier.MetricsScope);
        }

        private IWindowStore<Bytes, byte[]> WrapLogging(IWindowStore<Bytes, byte[]> inner)
        {
            if (!LoggingEnabled)
                return inner;

            return new ChangeLoggingWindowBytesStore(inner);
        }
    }
}