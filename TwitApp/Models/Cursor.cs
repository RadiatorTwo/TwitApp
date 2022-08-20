using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TwitApp.Models
{
    public class Cursor
    {
        [Key]
        public string Name { get; set; }
        public long CursorID { get; set; }
    }
}
