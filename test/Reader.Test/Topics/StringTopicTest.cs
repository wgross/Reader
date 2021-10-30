using Reader.Topics;
using Xunit;

namespace Reader.Test.Topics
{
    public class StringTopicTest
    {
        [Fact]
        public void StringTopic_receives_value()
        {
            // ACT
            var result = new StringTopic("value").AsString;

            // ASSERT
            Assert.Equal("value", result);
        }

        [Fact]
        public void StringTopic_are_equal_by_value()
        {
            // ARRANGE
            var topic1 = new StringTopic("value");
            var topic2 = new StringTopic("value");

            // ACT
            var result = topic1.Equals(topic2);

            // ASSERT
            Assert.True(result);
        }

        [Fact]
        public void StringTopic_arent_equal_by_value()
        {
            // ARRANGE
            var topic1 = new StringTopic("value");
            var topic2 = new StringTopic("different");

            // ACT
            var result = topic1.Equals(topic2);

            // ASSERT
            Assert.False(result);
        }

        [Fact]
        public void StringTopic_arent_equal_by_hashcode()
        {
            // ARRANGE
            var topic1 = new StringTopic("value");
            var topic2 = new StringTopic("different");

            // ACT
            var result = topic1.GetHashCode().Equals(topic2.GetHashCode());

            // ASSERT
            Assert.False(result);
        }

        [Fact]
        public void StringTopic_are_equal_by_hashcode()
        {
            // ARRANGE
            var topic1 = new StringTopic("value");
            var topic2 = new StringTopic("value");

            // ACT
            var result = topic1.GetHashCode().Equals(topic2.GetHashCode());

            // ASSERT
            Assert.True(result);
        }

        [Fact]
        public void StringTopic_uses_value_as_string_representationo()
        {
            // ACT
            var result = new StringTopic("value").ToString();

            // ASSERT
            Assert.Equal("value", result);
        }
    }
}