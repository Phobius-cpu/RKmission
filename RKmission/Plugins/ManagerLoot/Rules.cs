namespace ManagerLoot
{
    public class Rule
    {
        public string Name = "";
        public string Lql = "";
        public string Hql = "";
        public string Quantity = "";
        public string Exact = "";
        public string OneEach = "";
        public string BagName = "";

        public Rule() { }

        public Rule(string name, string lql, string hql, string quantity, string exact, string one, string bagName)
        {
            this.Name = name;
            this.Lql = lql;
            this.Hql = hql;
            this.Quantity = quantity;
            this.Exact = exact;
            this.OneEach = one;
            this.BagName = bagName;
        }
    }
}
