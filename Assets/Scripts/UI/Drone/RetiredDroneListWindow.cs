﻿using UnityEngine;

namespace Drones.UI
{
    using DataStreamer;
    using Utils;

    public class RetiredDroneListWindow : ObjectListWindow
    {
        public static RetiredDroneListWindow New() => PoolController.Get(WindowPool.Instance).Get<RetiredDroneListWindow>(null);

        public override ListElement TupleType { get; } = ListElement.RetiredDroneList;

        protected override Vector2 MaximizedSize { get; } = new Vector2(1000, 650);

        public override void OnNewSource(IDataSource source)
        {
            var element = RetiredDroneTuple.New(this);
            element.Source = source;
            DataReceivers.Add(source, element);
            ListChanged += element.OnListChange;
            OnContentChange();
        }

    }
}