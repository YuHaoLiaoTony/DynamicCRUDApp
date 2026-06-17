using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicCRUDApp
{
    public class ApiConfig
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string BaseUrl { get; set; }
        public Dictionary<string,string> Headers { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, ApiInfo> Apis { get; set; } = new Dictionary<string, ApiInfo>();
        public List<FieldInfo> Fields { get; set; } = new List<FieldInfo>();
    }

    public class ApiInfo
    {
        public string Url { get; set; }
        public string Method { get; set; }

        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    }

    public class FieldInfo
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public string Type { get; set; } // String, Number, DateTime
        public string UiType { get; set; } // TextBox, ComboBox, DateTimePicker
        public List<string> Options { get; set; }
        public bool ShowInList { get; set; }
        public bool Editable { get; set; }
        public bool Required { get; set; }
        public bool IsPK { get; set; }
    }
}
