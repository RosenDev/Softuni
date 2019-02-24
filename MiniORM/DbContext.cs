using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;

namespace MiniORM
{
    public abstract class DbContext
    {
        private readonly DatabaseConnection connection;
        private readonly Dictionary<Type, PropertyInfo> dbSetProperties;

        internal static readonly Type[] AllowedSqlTypes =
        {
            typeof(int),
            typeof(string),
            typeof(ulong),
            typeof(long),
            typeof(uint),
            typeof(decimal),
            typeof(bool),
            typeof(DateTime)
        };

        protected DbContext(string connectionString)
        {
            connection= new DatabaseConnection(connectionString);
            dbSetProperties = this.DiscoverDbSets();
            using (new ConnectionManager(connection))
            {
                this.InitializeDbSets();
            }

            this.MapAllRelations();
        }

      private void InitializeDbSets()
        {
            foreach (var dbSet in dbSetProperties)
            {
                var dbSetType = dbSet.Key;
                var dbSetProperty = dbSet.Value;
                var populateDbSetGeneric = typeof(DbContext)
                    .GetMethod("PopulateDbSet", BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(dbSetType);
                populateDbSetGeneric.Invoke(this, new object[] {dbSetProperty});
            }

        }

        private void PopulateDbSet<TEntity>(PropertyInfo dbSet)
        where TEntity:class ,new()
        {
            var entities = LoadTableEntities<TEntity>();
            var dbSetInstance=new DbSet<TEntity>(entities);
            ReflectionHelper.ReplaceBackingField(this,dbSet.Name,dbSetInstance);
        }
        
        private IList<TEntity> LoadTableEntities<TEntity>() where TEntity : class, new()
        {
            var table = typeof(TEntity);
            var columns = GetEntityColumnNames(table);
            var tableName = GetTableName(table);
            var fetchRows = connection.FetchResultSet<TEntity>(tableName, columns).ToArray();
            return fetchRows;

        }

        private string[] GetEntityColumnNames(Type table)
        {
            var tableName = GetTableName(table);
            var dbColumns = connection.FetchColumnNames(tableName);
            var columns = table.GetProperties()
                .Where(x => dbColumns.Contains(x.Name)
                            && !x.HasAttribute<NotMappedAttribute>() &&
                            AllowedSqlTypes.Contains(x.PropertyType))
                .Select(x => x.Name)
                .ToArray();
            return columns;


        }

        public void SaveChanges()
        {
            var dbSets = dbSetProperties.Select(x => x.Value.GetValue(this)).ToArray();
            foreach (IEnumerable<object> dbSet in dbSets)
            {
                var invalidEntities = dbSet.Where(x => !IsObjectValid(x)).ToArray();
                if (invalidEntities.Any())
                {
                    throw new InvalidOperationException($"{invalidEntities.Length} Invalid Entities found in {dbSet.GetType().Name}!");
                }
            }

            using (new ConnectionManager(connection))
            {
                using (var transaction= connection.StartTransaction() )
                {
                    foreach (IEnumerable dbSet in dbSets)
                    {
                        var dbSetType = dbSet.GetType().GetGenericArguments().First();
                        var persistMethod = typeof(DbContext)
                            .GetMethod("Persist", BindingFlags.Instance | BindingFlags.NonPublic)
                            .MakeGenericMethod(dbSetType);
                        try
                        {
                            persistMethod.Invoke(this, new object[] {dbSet});
                        }
                        catch (TargetInvocationException tie)
                        {

                            throw tie.InnerException;

                        }
                        catch (InvalidOperationException)
                        {
                            transaction.Rollback();
                            throw;
                        }
                        catch (SqlException)
                        {

                            transaction.Rollback();
                            throw;
                        }
                    }
                    transaction.Commit();
                }
            }

        }

        private void Persist<TEntity>(DbSet<TEntity> dbSet) where TEntity:class ,new()
        {
            var tableName = GetTableName(typeof(TEntity));
            var columns = connection.FetchColumnNames(tableName).ToArray();
            if (dbSet.ChangeTracker.Added.Any())
            {
                connection.InsertEntities(dbSet.ChangeTracker.Added,tableName,columns);
            }

            var modifiedEntities = dbSet.ChangeTracker.GetModifiedEntities(dbSet).ToArray();
            if (modifiedEntities.Any())
            {
                connection.UpdateEntities(dbSet.ChangeTracker.Removed,tableName,columns);

            }

            if (dbSet.ChangeTracker.Removed.Any())
            {
                connection.DeleteEntities(dbSet.ChangeTracker.Removed,tableName,columns);
            }
        }

        private string GetTableName(Type type)
        {
            var tableName = ((TableAttribute) Attribute.GetCustomAttribute(type, typeof(TableAttribute))).Name;
            if (tableName==null)
            {
                tableName = dbSetProperties[type].Name;
            }

            return tableName;

        }

        private bool IsObjectValid(object o)
        {
var validationContext= new ValidationContext(o);
                        var valErrors=new List<ValidationResult>();
            var valResult = Validator.TryValidateObject(o, validationContext, valErrors, true);
            return valResult;

        }

        private void MapAllRelations()
        {
            foreach (var dbSetProperty in dbSetProperties)
            {
                var dbSetType = dbSetProperty.Key;
                var mapRelationsGeneric = typeof(DbContext)
                    .GetMethod("MapRelations", BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(dbSetType);
                var dbSet = dbSetProperty.Value.GetValue(this);
                mapRelationsGeneric.Invoke(this, new[] {dbSet});
            }
        }

        private void MapRelations<TEntity>(DbSet<TEntity> dbSet)
        where  TEntity:class ,new()
        {
            var entityType = typeof(TEntity);
            MapNavigationProperties(dbSet);
            var collections = entityType.GetProperties()
                .Where(x => x.PropertyType.IsGenericType&&
                           x.PropertyType.GetGenericTypeDefinition()==typeof(ICollection<>))
                .ToArray();

            foreach (var collection in collections)
            {
                var collectionType = collection.PropertyType.GenericTypeArguments.First();
                var mapCollectionMethod = typeof(DbContext)
                    .GetMethod("MapCollection", BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(entityType, collectionType);
                mapCollectionMethod.Invoke(this, new object[] {dbSet, collection});

            }
        }

        private void MapCollection<TDbSet, TCollection>(DbSet<TDbSet>dbSet,PropertyInfo collectionProperty)
        where TDbSet:class ,new() where TCollection:class ,new()
        {

            var entityType = typeof(TDbSet);
            var collectionType = typeof(TCollection);
            var primaryKeys = collectionType.GetProperties()
                .Where(x => x.HasAttribute<KeyAttribute>()).ToArray();
            var primaryKey = primaryKeys.First();
            var foreignKey = entityType.GetProperties().First(x => x.HasAttribute<KeyAttribute>());
            var isManyToMany = primaryKeys.Length>2;
            if (isManyToMany)
            {
                primaryKey=collectionType.GetProperties()
                    .First(x=>collectionType
                                   .GetProperty(x.GetCustomAttribute<ForeignKeyAttribute>().Name)
                     .PropertyType==entityType);

                var navigationDbSet = (DbSet<TCollection>) this.dbSetProperties[collectionType].GetValue(this);
                foreach (var entity in dbSet)
                {
                    var primaryKeyValue = foreignKey.GetValue(entity);
                    var navigationEntities = navigationDbSet
                        .Where(x => primaryKey.GetValue(x).Equals(primaryKeyValue))
                        .ToArray();
                    ReflectionHelper.ReplaceBackingField(entity,collectionProperty.Name,navigationEntities);


                }


            }

        }
        private void MapNavigationProperties<TEntity>(DbSet<TEntity> dbSet)
            where TEntity : class, new()
        {
            var entityType = typeof(TEntity);
            var foreignKeys = entityType.GetProperties()
                .Where(x => x.HasAttribute<ForeignKeyAttribute>())
                .ToArray();

            foreach (var fKey in foreignKeys)
            {
                var navPropertyName = fKey.GetCustomAttribute<ForeignKeyAttribute>().Name;
                var navProperty = entityType.GetProperty(navPropertyName);
                var navDbSet = dbSetProperties[navProperty.PropertyType].GetValue(this);
                var navPrimaryKey = navProperty.PropertyType.GetProperties()
                    .First(x => x.HasAttribute<KeyAttribute>());
                foreach (var entity in dbSet)
                {
                    var fkValue = fKey.GetValue(entity);
                    var navPropertyValue = ((IEnumerable<object>) navDbSet)
                        .First(x=>navPrimaryKey.GetValue(x).Equals(fkValue));
                    navProperty.SetValue(entity,navPropertyValue);

                }
            }


        }

        private Dictionary<Type, PropertyInfo> DiscoverDbSets()
        {

            return GetType().GetProperties()
                .Where(x => x.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                .ToDictionary(x => x.PropertyType.GetGenericArguments().First(), x => x);
        }
    }
}