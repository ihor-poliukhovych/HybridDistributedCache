namespace HybridDistributedCache.Models
{
    public class MyComplexValue
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public MyValue[] Values { get; set; }

        public MyComplexValue()
        {

        }

        public MyComplexValue(int id, string name, MyValue[] values)
        {
            this.Id = id;
            this.Name = name;
            this.Values = values;
        }
    }
}