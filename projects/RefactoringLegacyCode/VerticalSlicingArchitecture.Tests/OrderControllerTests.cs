﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using RefactoringLegacyCode.Tests.Asserters;
using RefactoringLegacyCode.Tests.Shared;
using VerifyNUnit;

namespace RefactoringLegacyCode.Tests
{
    public class OrderControllerTests
    {
        [Test]
        public async Task XmlFileSnapShotTest()
        {
            //Arrange
            var testServer = new InMemoryServer();

            testServer.DateTimeProvider().Setup(provider => provider.Now).Returns(new DateTime(2024, 11, 7, 10, 10, 10));

            //Act
            var response = await testServer.Client().PostAsync("api/order/1/process", null);

            //Assert
            var filePath = Path.Combine(Environment.CurrentDirectory, "Order_1.xml");
            await Verifier.VerifyFile(filePath);
        }

        [Test]
        public async Task SendEmail_ShouldSendExpectedRequest()
        {
            // Arrange
            var testServer = new InMemoryServer();

            StringContent capturedContent = null;

            testServer.EmailSender().Setup(sender => sender.SendEmail(It.IsAny<StringContent>()))
                .Callback<StringContent>(content => capturedContent = content);

            // Act  
            var response = await testServer.Client().PostAsync("api/order/1/process", null);

            // Assert
            await HttpResponseAsserter.AssertThat(response).HasStatusCode(HttpStatusCode.OK);

            var actualEmailPayloadJson = await capturedContent.ReadAsStringAsync();
            var actualEmailPayload = JsonSerializer.Deserialize<Dictionary<string, string>>(actualEmailPayloadJson);

            actualEmailPayload["to"].Should().Be("customer@example.com");
            actualEmailPayload["subject"].Should().Be($"Order Confirmation - Order #1");
            actualEmailPayload["body"].Should().Be($"Dear Customer,\n\nThank you for your order #1. Your order has been processed and will be delivered soon.\n\nBest Regards,\nWarehouse Team");
            capturedContent.Headers.ContentType.MediaType.Should().Be("application/json");
            capturedContent.Headers.ContentType.CharSet.Should().Be("utf-8");
        }

        [TestCase(11, "Express", 10, 120)]
        [TestCase(11, "Express", 18, 150)]
        [TestCase(11, "Express", 19, 150)]
        [TestCase(11, "SameDay", 10, 180)]
        [TestCase(11, "SameDay", 14, 160)]
        [TestCase(11, "SameDay", 12, 160)]
        [TestCase(11, "Standard", 12, 80)]
        [TestCase(50, "Standard", 12, 80)]
        [TestCase(51, "Standard", 12, 100)]
        [TestCase(1, "Express", 10, 50)]
        [TestCase(5, "Express", 10, 50)]
        [TestCase(6, "Express", 10, 60)]
        [TestCase(6, "Express", 18, 70)]
        [TestCase(1, "SameDay", 10, 90)]
        [TestCase(1, "SameDay", 14, 110)]
        [TestCase(1, "Standard", 10, 20)]
        [TestCase(1, "Standard", 18, 40)]
        [TestCase(1, "Standard", 19, 40)]

        public void TestPriorityCalculation(int quantity, string deliveryType, int hour, int expectedPriority)
        {
            var orderDetails = new OrderDetails
            {
                Quantity = quantity,
                DeliveryType = deliveryType
            };

            var mockedDateTimeProvider = new Mock<IDateTimeProvider>();
            mockedDateTimeProvider.Setup(provider => provider.Now).Returns(new DateTime(2024, 11, 7, hour, 10, 10));

            var priority = OrderController.CalculatePriority(mockedDateTimeProvider.Object, orderDetails);

            priority.Should().Be(expectedPriority);
        }

        public static Arbitrary<string> DeliveryTypes()
        {
            var deliveryTypes = new[] { "Standard", "Express", "SameDay" };

            return Arb.From(Gen.Elements(deliveryTypes));
        }

        public static Arbitrary<int> QuantityBetween1And100()
        {
            return Arb.From(Gen.Choose(1, 100));
        }

        public static Arbitrary<int> Hours()
        {
            return Arb.From(Gen.Choose(1, 23));
        }

        [Test]
        public void HigherQuantity_ShouldIncreasePriority()
        {
            Configuration.Default.MaxNbOfTest = 100;

            Prop.ForAll(
                DeliveryTypes(),
                QuantityBetween1And100(),
                Hours(),
                (deliveryType, quantity, hour) =>
                {
                    var mockedDateTimeProvider = new Mock<IDateTimeProvider>();
                    mockedDateTimeProvider.Setup(provider => provider.Now).Returns(new DateTime(2024, 11, 7, hour, 10, 10));

                    var lowerQuantityPriority = OrderController.CalculatePriority(mockedDateTimeProvider.Object, new OrderDetails
                    {
                        Quantity = quantity,
                        DeliveryType = deliveryType
                    });

                    var higherQuantityPriority = OrderController.CalculatePriority(mockedDateTimeProvider.Object, new OrderDetails
                    {
                        Quantity = quantity + 1,
                        DeliveryType = deliveryType
                    });

                    return lowerQuantityPriority <= higherQuantityPriority;
                }
            ).VerboseCheckThrowOnFailure();
        }


        [Test]
        public void PriorityOrderShouldBeSameDayThenExpressThenStandard()
        {
            Configuration.Default.MaxNbOfTest = 100;

            Prop.ForAll(
                QuantityBetween1And100(),
                Hours(),
                (quantity, hour) =>
                {
                    var mockedDateTimeProvider = new Mock<IDateTimeProvider>();
                    mockedDateTimeProvider.Setup(provider => provider.Now).Returns(new DateTime(2024, 11, 7, hour, 10, 10));

                    var sameDayPriority = OrderController.CalculatePriority(mockedDateTimeProvider.Object, new OrderDetails
                    {
                        Quantity = quantity,
                        DeliveryType = "SameDay"
                    });

                    var expressPriority = OrderController.CalculatePriority(mockedDateTimeProvider.Object, new OrderDetails
                    {
                        Quantity = quantity,
                        DeliveryType = "Express"
                    });

                    var standardPriority = OrderController.CalculatePriority(mockedDateTimeProvider.Object, new OrderDetails
                    {
                        Quantity = quantity,
                        DeliveryType = "Standard"
                    });

                    return sameDayPriority > expressPriority && expressPriority > standardPriority;
                }
            ).VerboseCheckThrowOnFailure();
        }
    }
}
