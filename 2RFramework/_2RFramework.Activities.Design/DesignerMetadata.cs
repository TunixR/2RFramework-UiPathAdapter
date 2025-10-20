using _2RFramework.Activities.Design.Designers;
using _2RFramework.Activities.Design.Properties;
using System.Activities.Presentation.Metadata;
using System.ComponentModel;
using System.ComponentModel.Design;

namespace _2RFramework.Activities.Design
{
    public class DesignerMetadata : IRegisterMetadata
    {
        public void Register()
        {
            var builder = new AttributeTableBuilder();
            builder.ValidateTable();

            var categoryAttribute = new CategoryAttribute($"{Resources.Category}");

            builder.AddCustomAttributes(typeof(Task), categoryAttribute);
            builder.AddCustomAttributes(typeof(Task), new DesignerAttribute(typeof(TaskDesigner)));
            builder.AddCustomAttributes(typeof(Task), new HelpKeywordAttribute(""));


            MetadataStore.AddAttributeTable(builder.CreateTable());
        }
    }
}
