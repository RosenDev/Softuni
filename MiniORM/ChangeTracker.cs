using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

namespace MiniORM
{
    internal class ChangeTracker<T>
    where T: class ,new()
    {
        private readonly List<T> allEntities;
        private readonly List<T> added;
        private readonly List<T> removed;
        public IReadOnlyCollection<T> AllEntities => allEntities.AsReadOnly();
        public IReadOnlyCollection<T> Removed => removed.AsReadOnly();
        public IReadOnlyCollection<T> Added => added.AsReadOnly();
        public ChangeTracker(IEnumerable<T>entities)
        {
            
            this.added = new List<T>();
            this.removed = new List<T>();
            this.allEntities = CloneEntities(entities);
        }
        private static List<T> CloneEntities(IEnumerable<T>entities)
        {
            var clonedEntities= new List<T>();
            var propertiesToClone =
                typeof(T).GetProperties().Where(x => DbContext.AllowedSqlTypes.Contains(x.PropertyType)).ToArray();
            foreach (var entity in entities)
            {
                var clonedEntity = Activator.CreateInstance<T>();
                foreach (var propertyInfo in propertiesToClone)
                {
                    var value = propertyInfo.GetValue(entity);
                    propertyInfo.SetValue(clonedEntity, value);

                }

                clonedEntities.Add(clonedEntity);

            }

            return clonedEntities;
        }

        public void Add(T item) => this.added.Add(item);
        public void Remove(T item) => this.removed.Add(item);

        public IEnumerable<T> GetModifiedEntities(DbSet<T> dbSet)
        {
            var modifiedEntities=new List<T>();
            var primaryKeys = typeof(T).GetProperties().Where(x => x.HasAttribute<KeyAttribute>()).ToArray();
            foreach (var proxyEntity in AllEntities)
            {
                var primaryKeyValues = GetPrimaryKeyValues(primaryKeys, proxyEntity);
                var entity = dbSet.Entities.Single(e =>
                    GetPrimaryKeyValues(primaryKeys, e).SequenceEqual(primaryKeyValues));
                var isModified = IsModified(proxyEntity,entity);
                if (isModified)
                {
                    modifiedEntities.Add(entity);
                }
            }

            return modifiedEntities;
        }

        private static bool IsModified(T proxyEntity, T entity)
        {
            var monitoredProperties = typeof(T).GetProperties()
                .Where(x=>DbContext.AllowedSqlTypes
                    .Contains(x.PropertyType))
                .ToArray();
            var modifiedProperties = monitoredProperties
                .Where(x => !Equals(x.GetValue(entity), x.GetValue(proxyEntity)))
                .ToArray();
            var isModified = modifiedProperties.Any();
            return isModified;

        }


        private static IEnumerable<object> GetPrimaryKeyValues(IEnumerable<PropertyInfo> primaryKeys,T entity)
        {
            return primaryKeys.Select(pk => pk.GetValue(entity));
        }

    }
}