# Schema_Should_ExposeConnectionPageInfoAsSeparateType_When_ClassicPagingAndCustomConnectionAreMixed

## ConnectionPageInfo

```graphql
"Represents the connection page info.\nThis class provides additional information about pagination in a connection."
type ConnectionPageInfo {
  "Indicates whether more edges exist following\nthe set defined by the clients arguments."
  hasNextPage: Boolean! @cost(weight: "10")
  "Indicates whether more edges exist prior\nthe set defined by the clients arguments."
  hasPreviousPage: Boolean! @cost(weight: "10")
  "When paginating backwards, the cursor to continue."
  startCursor: String @cost(weight: "10")
  "When paginating forwards, the cursor to continue."
  endCursor: String @cost(weight: "10")
  "A list of cursors to continue paginating forwards."
  forwardCursors: [PageCursor!]! @cost(weight: "10")
  "A list of cursors to continue paginating backwards."
  backwardCursors: [PageCursor!]! @cost(weight: "10")
}
```

## CustomConnection

```graphql
type CustomConnection {
  edges: [CustomEdge!]
  pageInfo: ConnectionPageInfo!
}
```

## PageInfo

```graphql
"Information about pagination in a connection."
type PageInfo {
  "Indicates whether more edges exist following the set defined by the clients arguments."
  hasNextPage: Boolean! @cost(weight: "10")
  "Indicates whether more edges exist prior the set defined by the clients arguments."
  hasPreviousPage: Boolean! @cost(weight: "10")
  "When paginating backwards, the cursor to continue."
  startCursor: String @cost(weight: "10")
  "When paginating forwards, the cursor to continue."
  endCursor: String @cost(weight: "10")
  "A list of cursors to continue paginating forwards."
  forwardCursors: [PageCursor!]! @cost(weight: "10")
  "A list of cursors to continue paginating backwards."
  backwardCursors: [PageCursor!]! @cost(weight: "10")
}
```

## ProductsConnection

```graphql
"A connection to a list of items."
type ProductsConnection {
  "Information to aid in pagination."
  pageInfo: PageInfo!
  "A list of edges."
  edges: [ProductsEdge!]
  "A flattened list of the nodes."
  nodes: [Product!]
}
```
