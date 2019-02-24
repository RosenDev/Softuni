using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace MiniORM
{
    public class DbSet<TEntity> : ICollection<TEntity> where TEntity:class ,new()
    {
        internal IList<TEntity> Entities { get; set; }
        internal ChangeTracker<TEntity> ChangeTracker { get; set; }

        public DbSet(IList<TEntity> entities)
        {
            Entities = entities.ToList();
            this.ChangeTracker=new ChangeTracker<TEntity>(entities);
        }

        public void Add(TEntity item)
        {
            if (item==null)
            {
                throw new ArgumentNullException(nameof(item),"Item cannot be null!");
            }
            this.Entities.Add(item);
            this.ChangeTracker.Add(item);
        }

        

        public void Clear()
        {
            while (this.Entities.Any())
            {
                var entity = this.Entities.First();
                this.Remove(entity);
            }
        }

        public int Count => Entities.Count;
        public bool IsReadOnly => Entities.IsReadOnly;
        public void CopyTo(TEntity[] array, int arrIndex)
        {
            Entities.CopyTo(array,arrIndex);
        }
        public bool Contains(TEntity item)
        {
            return Entities.Contains(item);
        }
        
       public bool Remove(TEntity item)
        {
            if (item==null)
            {
                throw new ArgumentNullException(nameof(item), "Item cannot be null!");
            }

            var removedSuccessfully = this.Entities.Remove(item);
            if (removedSuccessfully)
            {
                ChangeTracker.Remove(item);
            }

            return removedSuccessfully;
        }

        public IEnumerator<TEntity> GetEnumerator() => Entities.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void RemoveRange(IEnumerable<TEntity> entities)
        {
            foreach (var entity in entities)
            {
                this.Remove(entity);
            }
        }
    }
}