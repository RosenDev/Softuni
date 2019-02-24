using System;
using System.Linq;
using MiniORM.App.Data;
using MiniORM.App.Data.Entities;

namespace MiniORM.App
{
    class StartUp
    {
        static void Main(string[] args)
        {
            var connString = "Server=DESKTOP-CUCRL15\\SQLEXPRESS;Database=MiniORM;Integrated Security = true";
            var context = new SoftUniDbContext(connString);
            context.Employees.Add(
                new Employee
                {
                FirstName = "Gosho",
               LastName = "Inserted",
        DepartmentId = context.Departments.First().Id,
                    IsEmployed = true
                });
            var employee = context.Employees.Last();
            employee.FirstName = "Modified";
            context.SaveChanges();
        }
    }
}
