using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net.Cache;

namespace MiniORM.App.Data.Entities
{[Table(nameof(Project)+"s")]
    public class Project
    {[Key]
        public int Id { get; set; }
        [Required]
        public string Name { get; set; }

        public ICollection<EmployeeProject> EmployeeProjects { get;}

    }
}