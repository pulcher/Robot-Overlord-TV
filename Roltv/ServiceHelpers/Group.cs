using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ProjectOxford.Face.Contract;

namespace ServiceHelpers
{
    public class Group
    {
        public static PersonGroup HeroVillanGroupExists(PersonGroup[] groups)
        {
            var foundGroup = groups.Where(x => x.Name == GroupData.HeroVillanGroupName);

            return foundGroup.FirstOrDefault();
        }

        public static bool IsHero (Person person)
        {
            return GroupData.Heros.Contains(person.Name);
        }
    }
}
