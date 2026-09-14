export type RagLibraryDocument = {
  id: string
  title: string
  source: string
  category: string
  subCategory: string
  documentType: string
  context: string
  content: string
  resolution: string
  severity: string
  markdown: string
  createdAt: string
}

export type RagLibraryListResponse = {
  items: RagLibraryDocument[]
  totalCount: number
  page: number
  pageSize: number
}

export type RagLibraryCategoryPair = {
  category: string
  subCategory: string
  context: string
}

export type RagViewMode = 'all' | 'ops' | 'library'
