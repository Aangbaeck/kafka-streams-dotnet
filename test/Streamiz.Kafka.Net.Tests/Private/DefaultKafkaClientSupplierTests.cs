﻿using NUnit.Framework;
using Streamiz.Kafka.Net.Kafka.Internal;
using System;
using Streamiz.Kafka.Net.Processors.Internal;

namespace Streamiz.Kafka.Net.Tests.Private
{
    public class DefaultKafkaClientSupplierTest
    {
        private readonly StreamConfig config = GetConfig();

        private static StreamConfig GetConfig()
        {
            var config = new StreamConfig();
            config.ApplicationId = "test-DefaultKafkaClientSupplierTest";
            return config;
        }

        [Test]
        public void ShouldArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new DefaultKafkaClientSupplier(null));
        }

        [Test]
        public void CreateAdminClient()
        {
            var supplier = new DefaultKafkaClientSupplier(new KafkaLoggerAdapter(config));
            var adminClient = supplier.GetAdmin(config.ToAdminConfig("admin"));
            Assert.IsNotNull(adminClient);
            Assert.AreEqual("admin", adminClient.Name.Split("#")[0]);
        }

        [Test]
        public void CreateConsumerClient()
        {
            var supplier = new DefaultKafkaClientSupplier(new KafkaLoggerAdapter(config));
            var consumer = supplier.GetConsumer(config.ToConsumerConfig("consume"), new StreamsRebalanceListener(null));
            Assert.IsNotNull(consumer);
            Assert.AreEqual("consume", consumer.Name.Split("#")[0]);
        }

        [Test]
        public void CreateRestoreClient()
        {
            var supplier = new DefaultKafkaClientSupplier(new KafkaLoggerAdapter(config));
            var restore = supplier.GetRestoreConsumer(config.ToConsumerConfig("retore"));
            Assert.IsNotNull(restore);
            Assert.AreEqual("retore", restore.Name.Split("#")[0]);
        }

        [Test]
        public void CreateProducerClient()
        {
            var supplier = new DefaultKafkaClientSupplier(new KafkaLoggerAdapter(config));
            var produce = supplier.GetProducer(config.ToProducerConfig("produce"));
            Assert.IsNotNull(produce);
            Assert.AreEqual("produce", produce.Name.Split("#")[0]);
        }

        [Test]
        public void CreateConsumerWithStats()
        {
            config.ExposeLibrdKafkaStats = true;
            config.ApplicationId = "test-app";
            config.ClientId = "test-client";
            var supplier = new DefaultKafkaClientSupplier(new KafkaLoggerAdapter(config));

            var consumerConfig = config.ToConsumerConfig("consume");
            StreamizConsumerConfig wrapper = new StreamizConsumerConfig(consumerConfig, "thread-1");

            var consumer = supplier.GetConsumer(wrapper, new StreamsRebalanceListener(null));
            Assert.IsNotNull(consumer);
        }
        
        [Test]
        public void CreateProducerWithStats()
        {
            config.ExposeLibrdKafkaStats = true;
            config.ApplicationId = "test-app";
            config.ClientId = "test-client";
            var supplier = new DefaultKafkaClientSupplier(new KafkaLoggerAdapter(config));

            var producerConfig = config.ToProducerConfig("producer");
            StreamizProducerConfig wrapper = new StreamizProducerConfig(producerConfig, "thread-1", new TaskId(){Id = 0, Partition = 0});

            var producer = supplier.GetProducer(wrapper);
            Assert.IsNotNull(producer);
        }
    }
}